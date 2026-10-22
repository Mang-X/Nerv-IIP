using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class SchedulingReleaseMesRevocationAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoke_reloads_all_three_operations_and_withdraws_only_unstarted_dispatch(bool pauseStartedTask)
    {
        // #4275: a revoke request runs in a fresh scope, without release-time EF relationship fixup.
        var now = DateTimeOffset.Parse("2026-07-18T04:00:00Z");
        var schedulingOptions = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseInMemoryDatabase($"schedule-revoke-reload-{Guid.CreateVersion7():N}").Options;
        SchedulePlanReleasedIntegrationEvent released;
        await using (var seedDb = new SchedulingDbContext(schedulingOptions, new NoopMediator()))
        {
            var plan = CreatePlan("plan-three-operations", "DEV-1", now.AddHours(1), 3);
            seedDb.SchedulePlans.Add(plan);
            await seedDb.SaveChangesAsync();
            await new ReleaseSchedulePlanCommandHandler(seedDb, new FixedTimeProvider(now), new NoopReleaseScopeLock())
                .Handle(new ReleaseSchedulePlanCommand(plan.PlanId, "org-001", "env-dev"), CancellationToken.None);
            released = ConvertReleased(plan, now);
            await seedDb.SaveChangesAsync();
        }

        SchedulePlanRevokedIntegrationEvent revoked;
        await using (var revokeDb = new SchedulingDbContext(schedulingOptions, new NoopMediator()))
        {
            var response = await new RevokeSchedulePlanCommandHandler(
                    revokeDb, new FixedTimeProvider(now.AddHours(2)), new NoopReleaseScopeLock())
                .Handle(new RevokeSchedulePlanCommand(released.Payload.PlanId, "org-001", "env-dev"), CancellationToken.None);
            var plan = Assert.Single(revokeDb.SchedulePlans.Local);
            revoked = ConvertRevoked(plan, now.AddHours(2));
            Assert.Equal(released.Payload.PlanId, revoked.Payload.PlanId);
            Assert.Equal(response.ReleaseRevision, revoked.Payload.ReleaseRevision);
            Assert.Equal(released.Payload.AffectedOperations.OrderBy(x => x.OperationId),
                revoked.Payload.AffectedOperations.OrderBy(x => x.OperationId));
            Assert.Equal(3, revoked.Payload.AffectedOperations.Count);
            await revokeDb.SaveChangesAsync();
        }

        var mesOptions = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"schedule-three-operations-mes-{Guid.CreateVersion7():N}").Options;
        await using var mesDb = new MesDbContext(mesOptions, new NoopMediator());
        mesDb.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 1, now.AddDays(1), "PCS"));
        await mesDb.SaveChangesAsync();
        await new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
                mesDb, new InMemoryIntegrationEventDeadLetterStore(), new PostgreSqlMesScheduleReleaseScopeCoordinator(mesDb))
            .HandleAsync(released, CancellationToken.None);
        await mesDb.SaveChangesAsync();
        var started = await mesDb.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        started.Start(now.AddHours(1));
        if (pauseStartedTask)
        {
            started.Pause(now.AddMinutes(90));
        }
        await mesDb.SaveChangesAsync();

        var handler = new SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(
            mesDb, new InMemoryIntegrationEventDeadLetterStore(), new PostgreSqlMesScheduleReleaseScopeCoordinator(mesDb));
        await handler.HandleAsync(revoked, CancellationToken.None);
        await handler.HandleAsync(revoked, CancellationToken.None);
        mesDb.ChangeTracker.Clear();
        var tasks = await mesDb.OperationTasks.OrderBy(x => x.OperationSequence).ToArrayAsync();
        Assert.Equal(3, tasks.Length);
        Assert.All(tasks, task =>
        {
            Assert.Null(task.SchedulePlanId);
            Assert.Null(task.ScheduleReleaseRevision);
            Assert.Null(task.ScheduledAtUtc);
        });
        Assert.Equal(pauseStartedTask ? OperationTaskLifecycleStatus.Paused : OperationTaskLifecycleStatus.InProgress, tasks[0].Status);
        Assert.Equal(now.AddHours(1), tasks[0].ExistingStartUtc);
        Assert.Equal("DEV-1", tasks[0].DeviceAssetId);
        Assert.Equal(now, tasks[0].AssignedAtUtc);
        Assert.All(tasks.Skip(1), task =>
        {
            Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
            Assert.Null(task.DeviceAssetId);
            Assert.Null(task.AssignedAtUtc);
            Assert.Equal("explicit", task.ScheduleInvalidationReasonCode);
        });
        var receipt = await mesDb.ProcessedIntegrationEvents.SingleAsync(x => x.EventId == revoked.EventId);
        Assert.Equal(SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch.ConsumerName, receipt.ConsumerName);
        Assert.Equal(2, await mesDb.ProcessedIntegrationEvents.CountAsync());
        var watermark = await mesDb.ScheduleReleaseWatermarks.SingleAsync();
        Assert.Equal(revoked.Payload.PlanId, watermark.RevokedPlanId);
        Assert.Equal(revoked.Payload.ReleaseRevision, watermark.RevokedReleaseRevision);
    }

    [Fact]
    public async Task Real_scheduling_release_and_revoke_events_converge_mes_to_the_latest_plan_idempotently()
    {
        var now = DateTimeOffset.Parse("2026-07-18T04:00:00Z");
        var plan1 = CreatePlan("plan-1", "DEV-1", now.AddHours(1));
        plan1.Release(now, 1);
        var release1 = ConvertReleased(plan1, now);
        plan1.ClearDomainEvents();

        var plan2 = CreatePlan("plan-2", "DEV-2", now.AddHours(3));
        plan1.Supersede(plan2.PlanId, now.AddMinutes(1));
        var revoke1 = ConvertRevoked(plan1, now.AddMinutes(1));
        plan1.ClearDomainEvents();
        plan2.Release(now.AddMinutes(1), 2);
        var release2 = ConvertReleased(plan2, now.AddMinutes(1));
        plan2.ClearDomainEvents();

        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"schedule-release-mes-acceptance-{Guid.CreateVersion7():N}")
            .Options;
        await using var mesDb = new MesDbContext(options, new NoopMediator());
        mesDb.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 1, now.AddDays(1), "PCS"));
        await mesDb.SaveChangesAsync();
        var releasedHandler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            new PostgreSqlMesScheduleReleaseScopeCoordinator(mesDb));
        var revokedHandler = new SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            new PostgreSqlMesScheduleReleaseScopeCoordinator(mesDb));

        await releasedHandler.HandleAsync(release1, CancellationToken.None);
        await mesDb.SaveChangesAsync();
        await releasedHandler.HandleAsync(release2, CancellationToken.None);
        await mesDb.SaveChangesAsync();
        await revokedHandler.HandleAsync(revoke1, CancellationToken.None);

        var current = await mesDb.OperationTasks.SingleAsync();
        Assert.Equal("plan-2", current.SchedulePlanId);
        Assert.Equal(2, current.ScheduleReleaseRevision);
        Assert.Equal("DEV-2", current.DeviceAssetId);

        plan2.Revoke(now.AddMinutes(2));
        var revoke2 = ConvertRevoked(plan2, now.AddMinutes(2));
        await revokedHandler.HandleAsync(revoke2, CancellationToken.None);
        await revokedHandler.HandleAsync(revoke2, CancellationToken.None);

        var revoked = await mesDb.OperationTasks.SingleAsync();
        Assert.Null(revoked.SchedulePlanId);
        Assert.Null(revoked.ScheduleReleaseRevision);
        Assert.Null(revoked.DeviceAssetId);
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, revoked.Status);
        Assert.Equal(4, await mesDb.ProcessedIntegrationEvents.CountAsync());
    }

    private static SchedulePlan CreatePlan(string planId, string resourceId, DateTimeOffset startUtc, int operationCount = 1)
    {
        var contract = new SchedulePlanContract(
            1,
            planId,
            $"problem-{planId}",
            $"fingerprint-{planId}",
            "aps-lite-v1",
            SchedulePlanStatusContract.Generated,
            startUtc.AddHours(-1),
            new SchedulePlanMetricsContract(1, 0, 60, 60, 0, 0, 1m, 0m),
            Enumerable.Range(1, operationCount).Select(index => new ScheduleAssignmentContract(
                $"assignment-{planId}-{index}", "WO-001", $"OP-{index * 10}", index * 10, resourceId, "WC-1",
                startUtc.AddHours(index - 1), startUtc.AddHours(index), false, "scheduled")).ToArray(),
            [], [], [], [], []);
        var plan = SchedulePlan.FromGeneratedPlan("org-001", "env-dev", SchedulePlanContractMapper.ToDomainSnapshot(contract));
        plan.ClearDomainEvents();
        return plan;
    }

    private static SchedulePlanReleasedIntegrationEvent ConvertReleased(SchedulePlan plan, DateTimeOffset now) =>
        new SchedulePlanReleasedIntegrationEventConverter(
                new FixedTimeProvider(now),
                new StubContextAccessor())
            .Convert(Assert.IsType<SchedulePlanReleasedDomainEvent>(Assert.Single(plan.GetDomainEvents())));

    private static SchedulePlanRevokedIntegrationEvent ConvertRevoked(SchedulePlan plan, DateTimeOffset now) =>
        new SchedulePlanRevokedIntegrationEventConverter(
                new FixedTimeProvider(now),
                new StubContextAccessor())
            .Convert(Assert.IsType<SchedulePlanRevokedDomainEvent>(Assert.Single(plan.GetDomainEvents())));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopReleaseScopeLock : IScheduleReleaseScopeLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> AcquireAsync(string organizationId, string environmentId, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(this);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext() => new("corr-701", "cause-701", "user:planner-1");
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
