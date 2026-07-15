using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesDispatchSchedulingOverrideAcceptanceTests
{
    [Fact]
    public async Task Clearing_real_mes_dispatch_unlocks_the_next_scheduling_run_for_optimization()
    {
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        await using var mesDb = new MesDbContext(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseInMemoryDatabase($"mes-dispatch-acceptance-{Guid.NewGuid():N}").Options,
            new NoopMediator());
        mesDb.OperationTasks.Add(OperationTask.Queue(
            "org-1", "env-1", "WO-1", "OP-10", 10, "WC-1", [], start, TimeSpan.FromHours(1)));
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        var dispatchHandler = new AssignDispatchTaskCommandHandler(mesDb);

        var assignDevice2 = new AssignDispatchTaskCommand(
            "org-1", "env-1", "OP-10", "operator-1", "DEVICE-2", "SHIFT-1",
            start.AddMinutes(-5), "user:planner-1");
        var clearDevice = new AssignDispatchTaskCommand(
            "org-1", "env-1", "OP-10", "operator-1", null, "SHIFT-1",
            start.AddMinutes(-4), "user:planner-1");

        await dispatchHandler.Handle(assignDevice2, CancellationToken.None);
        var mesTask = await mesDb.OperationTasks.SingleAsync();
        var dispatched = ConvertSingleDispatch(mesTask);
        Assert.Equal("user:planner-1", dispatched.Actor);
        Assert.Equal(1, dispatched.Payload.DispatchRevision);
        mesTask.ClearDomainEvents();
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-scheduling-acceptance-{Guid.NewGuid():N}").Options;
        await using var db = new SchedulingDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var dispatchConsumer = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(db, deadLetters);
        var clearConsumer = new MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride(db, deadLetters);
        var overlay = new SchedulingOperationOverrideOverlay(db);
        var scheduler = new FiniteCapacityScheduler();

        await dispatchConsumer.HandleAsync(dispatched, CancellationToken.None);
        var lockedProblem = await overlay.ApplyAsync(CreateProblem(start), CancellationToken.None);
        var lockedPlan = scheduler.Schedule(lockedProblem, "plan-before-clear", start.AddMinutes(-1));
        var lockedAssignment = Assert.Single(lockedPlan.Assignments);
        Assert.True(lockedAssignment.IsLocked);
        Assert.Equal("DEVICE-2", lockedAssignment.ResourceId);

        await dispatchHandler.Handle(clearDevice, CancellationToken.None);
        mesTask = await mesDb.OperationTasks.SingleAsync();
        var cleared = ConvertSingleClear(mesTask, new MesIntegrationEventContext("corr-clear-2", dispatched.EventId));
        Assert.Equal(2, cleared.Payload.DispatchRevision);
        mesTask.ClearDomainEvents();
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        await clearConsumer.HandleAsync(cleared, CancellationToken.None);

        var overlaid = await overlay.ApplyAsync(CreateProblem(start), CancellationToken.None);
        var plan = scheduler.Schedule(overlaid, "plan-after-clear", start.AddMinutes(-1));

        var assignment = Assert.Single(plan.Assignments);
        Assert.False(assignment.IsLocked);
        Assert.Equal(0, plan.Metrics.LockedOperationCount);
        Assert.Equal(1, plan.Metrics.OptimizableOperationCount);
        Assert.NotEqual("DEVICE-2", assignment.ResourceId);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(1, await db.ScheduleOperationOverrides.CountAsync());
        Assert.False((await db.ScheduleOperationOverrides.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Newer_real_mes_redispatch_stays_locked_after_old_dispatch_and_clear_are_replayed()
    {
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        await using var mesDb = new MesDbContext(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseInMemoryDatabase($"mes-redispatch-acceptance-{Guid.NewGuid():N}").Options,
            new NoopMediator());
        mesDb.OperationTasks.Add(OperationTask.Queue(
            "org-1", "env-1", "WO-1", "OP-10", 10, "WC-1", [], start, TimeSpan.FromHours(1)));
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        var dispatchHandler = new AssignDispatchTaskCommandHandler(mesDb);

        var schedulingOptions = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseInMemoryDatabase($"mes-redispatch-scheduling-acceptance-{Guid.NewGuid():N}").Options;
        await using var schedulingDb = new SchedulingDbContext(schedulingOptions, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var dispatchConsumer = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(schedulingDb, deadLetters);
        var clearConsumer = new MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride(schedulingDb, deadLetters);

        await dispatchHandler.Handle(new AssignDispatchTaskCommand(
            "org-1", "env-1", "OP-10", "operator-1", "DEVICE-2", "SHIFT-1",
            start.AddMinutes(-5), "user:planner-1"), CancellationToken.None);
        var mesTask = await mesDb.OperationTasks.SingleAsync();
        var dispatchRevision1 = ConvertSingleDispatch(mesTask);
        mesTask.ClearDomainEvents();
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        await dispatchConsumer.HandleAsync(dispatchRevision1, CancellationToken.None);

        await dispatchHandler.Handle(new AssignDispatchTaskCommand(
            "org-1", "env-1", "OP-10", "operator-1", null, "SHIFT-1",
            start.AddMinutes(-4), "user:planner-1"), CancellationToken.None);
        mesTask = await mesDb.OperationTasks.SingleAsync();
        var clearRevision2 = ConvertSingleClear(
            mesTask, new MesIntegrationEventContext("corr-clear-2", dispatchRevision1.EventId));
        mesTask.ClearDomainEvents();
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        await clearConsumer.HandleAsync(clearRevision2, CancellationToken.None);

        await dispatchHandler.Handle(new AssignDispatchTaskCommand(
            "org-1", "env-1", "OP-10", "operator-2", "DEVICE-1", "SHIFT-1",
            start.AddMinutes(-3), "user:planner-2"), CancellationToken.None);
        mesTask = await mesDb.OperationTasks.SingleAsync();
        var dispatchRevision3 = ConvertSingleDispatch(mesTask);
        mesTask.ClearDomainEvents();
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        await dispatchConsumer.HandleAsync(dispatchRevision3, CancellationToken.None);

        await dispatchConsumer.HandleAsync(dispatchRevision1, CancellationToken.None);
        await clearConsumer.HandleAsync(clearRevision2, CancellationToken.None);

        var overlaid = await new SchedulingOperationOverrideOverlay(schedulingDb)
            .ApplyAsync(CreateProblem(start), CancellationToken.None);
        var plan = new FiniteCapacityScheduler().Schedule(overlaid, "plan-after-redispatch", start.AddMinutes(-1));
        var assignment = Assert.Single(plan.Assignments);

        Assert.True(assignment.IsLocked);
        Assert.Equal("DEVICE-1", assignment.ResourceId);
        Assert.Equal(1, plan.Metrics.LockedOperationCount);
        Assert.Equal(0, plan.Metrics.OptimizableOperationCount);
        var persisted = await schedulingDb.ScheduleOperationOverrides.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(3, persisted.SourceRevision);
        Assert.Equal(dispatchRevision3.EventId, persisted.SourceEventId);
        Assert.Equal(3, await schedulingDb.ProcessedIntegrationEvents.CountAsync());
    }

    private static MesOperationTaskManuallyDispatchedIntegrationEvent ConvertSingleDispatch(OperationTask task) =>
        new OperationTaskManuallyDispatchedIntegrationEventConverter().Convert(
            Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(Assert.Single(task.GetDomainEvents())));

    private static MesOperationTaskManualDispatchClearedIntegrationEvent ConvertSingleClear(
        OperationTask task,
        MesIntegrationEventContext context) =>
        new OperationTaskManualDispatchClearedIntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(context))
            .Convert(Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
                Assert.Single(task.GetDomainEvents())));

    private static SchedulingProblemContract CreateProblem(DateTimeOffset start) => new(
        1, "problem-next", "org-1", "env-1", start, start.AddHours(8),
        [new SchedulingOrderContract("WO-1", "SKU-1", 1m, start.AddHours(4), 1, false,
            [new SchedulingOperationContract("OP-10", 10, [], 60, "CAP-1", ["DEVICE-1", "DEVICE-2"],
                "DEVICE-1", start, start.AddHours(4), 1, false, ScheduleSplitPolicyContract.NonSplittable,
                start, null, "MES:OP-10")])],
        [
            new SchedulingResourceContract("DEVICE-1", "WC-1", ["CAP-1"], 1, "CAL-1", "01"),
            new SchedulingResourceContract("DEVICE-2", "WC-1", ["CAP-1"], 1, "CAL-1", "02")
        ],
        [new SchedulingCalendarContract("CAL-1", [new SchedulingTimeWindowContract(start, start.AddHours(8), "shift")])],
        [], [], [], []);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubMesIntegrationEventContextAccessor(MesIntegrationEventContext context)
        : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => context;
    }
}
