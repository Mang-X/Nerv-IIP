using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class ListSchedulePlansQueryHandlerTests
{
    [Fact]
    public async Task List_returns_superseded_and_revoked_terminal_statuses()
    {
        await using var dbContext = CreateDbContext();
        var superseded = CreatePlan("plan-superseded", SchedulePlanStatusContract.Generated);
        superseded.Release(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), 1);
        superseded.Supersede("plan-successor", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var revoked = CreatePlan("plan-revoked", SchedulePlanStatusContract.Generated);
        revoked.Release(new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero), 2);
        revoked.Revoke(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        dbContext.SchedulePlans.AddRange(superseded, revoked);
        await dbContext.SaveChangesAsync();

        var results = await new ListSchedulePlansQueryHandler(dbContext).Handle(
            new ListSchedulePlansQuery("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Superseded, Assert.Single(results, x => x.PlanId == "plan-superseded").Status);
        Assert.Equal(SchedulePlanStatusContract.Revoked, Assert.Single(results, x => x.PlanId == "plan-revoked").Status);
    }

    // Asserts the enrichment logic + newest-of-multiple selection. This handler cannot run on SQLite
    // (the plans query ORDER BYs GeneratedAtUtc, a DateTimeOffset SQLite refuses to sort), so the real
    // relational translation of the bounded anti-join is covered by SchedulingListPlansPostgresProfileTests.
    [Fact]
    public async Task List_marks_plans_with_recorded_invalidations_and_surfaces_latest_reason()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-clean", SchedulePlanStatusContract.Generated));
        var released = CreatePlan("plan-invalid", SchedulePlanStatusContract.Generated);
        released.Release(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), 1);
        dbContext.SchedulePlans.Add(released);

        // An older material-readiness invalidation, then a newer equipment invalidation for the same plan.
        dbContext.SchedulePlanInvalidations.Add(CreateInvalidation(
            "plan-invalid",
            reasonCode: SchedulingPlanInvalidationReasons.MaterialReadinessChanged,
            occurredAtUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            recordedAtUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 5, TimeSpan.Zero)));
        dbContext.SchedulePlanInvalidations.Add(CreateInvalidation(
            "plan-invalid",
            reasonCode: SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            occurredAtUtc: new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero),
            recordedAtUtc: new DateTimeOffset(2026, 6, 1, 11, 30, 5, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var handler = new ListSchedulePlansQueryHandler(dbContext);
        var results = await handler.Handle(
            new ListSchedulePlansQuery("org-001", "env-dev"),
            CancellationToken.None);

        var clean = Assert.Single(results, x => x.PlanId == "plan-clean");
        Assert.False(clean.IsInvalidated);
        Assert.Null(clean.LatestInvalidationReasonCode);
        Assert.Null(clean.LatestInvalidatedAtUtc);

        var invalid = Assert.Single(results, x => x.PlanId == "plan-invalid");
        Assert.True(invalid.IsInvalidated);
        Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalid.LatestInvalidationReasonCode);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero), invalid.LatestInvalidatedAtUtc);
    }

    private static SchedulePlanInvalidation CreateInvalidation(
        string planId,
        string reasonCode,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc)
    {
        return SchedulePlanInvalidation.Create(
            "org-001",
            "env-dev",
            planId,
            sourceEventId: $"evt-{reasonCode}-{recordedAtUtc.Ticks}",
            sourceEventType: "maintenance.AssetUnavailable",
            sourceService: "maintenance",
            reasonCode: reasonCode,
            affectedResourceId: "ASSET-CNC-01",
            affectedWorkOrderId: null,
            affectedOperationId: null,
            affectedSkuCode: null,
            occurredAtUtc: occurredAtUtc,
            recordedAtUtc: recordedAtUtc);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-list-plans-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static SchedulePlan CreatePlan(string planId, SchedulePlanStatusContract status)
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: "problem-001",
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: status,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(
                    ScheduledOperationCount: 1,
                    UnscheduledOperationCount: 0,
                    AssignedMinutes: 60,
                    MakespanMinutes: 60,
                    TotalTardinessMinutes: 0,
                    LateOperationCount: 0,
                    OnTimeRate: 1m,
                    AverageResourceUtilization: 0m),
                Assignments:
                [
                    new ScheduleAssignmentContract(
                        AssignmentId: $"assign-{planId}",
                        OrderId: "WO-001",
                        OperationId: "OP-001",
                        OperationSequence: 10,
                        ResourceId: "ASSET-CNC-01",
                        WorkCenterId: "WC-CNC",
                        StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        IsLocked: false,
                        ExplanationCode: "scheduled")
                ],
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
