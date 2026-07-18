using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class SchedulingReleaseMesRevocationAcceptanceTests
{
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

    private static SchedulePlan CreatePlan(string planId, string resourceId, DateTimeOffset startUtc)
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
            [new ScheduleAssignmentContract(
                $"assignment-{planId}", "WO-001", "OP-10", 10, resourceId, "WC-1",
                startUtc, startUtc.AddHours(1), false, "scheduled")],
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
