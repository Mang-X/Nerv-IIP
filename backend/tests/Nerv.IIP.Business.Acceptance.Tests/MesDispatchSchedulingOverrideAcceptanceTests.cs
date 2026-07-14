using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesDispatchSchedulingOverrideAcceptanceTests
{
    [Fact]
    public async Task Manual_mes_dispatch_is_preserved_by_the_next_scheduling_run()
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

        await dispatchHandler.Handle(new AssignDispatchTaskCommand(
            "org-1", "env-1", "OP-10", null, "DEVICE-2", "SHIFT-1", start.AddMinutes(-5)),
            CancellationToken.None);

        var persistedTask = await mesDb.OperationTasks.SingleAsync();
        var domainEvent = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(persistedTask.GetDomainEvents().Single());
        var integrationEvent = new OperationTaskManuallyDispatchedIntegrationEventConverter().Convert(domainEvent);
        await mesDb.SaveChangesAsync();
        mesDb.ChangeTracker.Clear();
        var reloadedTask = await mesDb.OperationTasks.SingleAsync();
        Assert.Equal("DEVICE-2", reloadedTask.DeviceAssetId);

        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseInMemoryDatabase($"mes-dispatch-scheduling-acceptance-{Guid.NewGuid():N}").Options;
        await using var db = new SchedulingDbContext(options, new NoopMediator());
        var handler = new MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
            db, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var problem = CreateProblem(start);
        var overlaid = await new SchedulingOperationOverrideOverlay(db).ApplyAsync(problem, CancellationToken.None);
        var plan = new FiniteCapacityScheduler().Schedule(overlaid, "plan-next", start.AddMinutes(-1));

        var assignment = Assert.Single(plan.Assignments);
        Assert.True(assignment.IsLocked);
        Assert.Equal("DEVICE-2", assignment.ResourceId);
        Assert.Equal(start, assignment.StartUtc);
        Assert.Equal(start.AddHours(1), assignment.EndUtc);
        Assert.Equal(1, plan.Metrics.LockedOperationCount);
        Assert.Equal(0, plan.Metrics.OptimizableOperationCount);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(1, await db.ScheduleOperationOverrides.CountAsync());
    }

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
}
