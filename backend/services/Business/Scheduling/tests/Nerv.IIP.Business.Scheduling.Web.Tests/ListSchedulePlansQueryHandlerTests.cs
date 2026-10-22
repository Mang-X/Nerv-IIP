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

    [Fact]
    public async Task History_filters_all_candidates_before_total_and_page_and_keeps_scope()
    {
        await using var dbContext = CreateDbContext();
        var releasedAt = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 105; index++)
        {
            var plan = CreatePlan($"history-{index:000}", SchedulePlanStatusContract.Generated);
            plan.Release(releasedAt.AddMinutes(index), index + 1);
            plan.Revoke(releasedAt.AddDays(1));
            dbContext.SchedulePlans.Add(plan);
            if (index % 2 == 0)
            {
                dbContext.SchedulePlanInvalidations.Add(CreateInvalidation(plan.PlanId, "older", releasedAt, releasedAt));
                dbContext.SchedulePlanInvalidations.Add(CreateInvalidation(plan.PlanId, "latest", releasedAt.AddMinutes(1), releasedAt.AddMinutes(1)));
            }
        }
        var otherOrg = CreatePlan("other-org", SchedulePlanStatusContract.Generated, "org-other");
        otherOrg.Release(releasedAt, 1);
        otherOrg.Revoke(releasedAt.AddDays(1));
        var otherEnv = CreatePlan("other-env", SchedulePlanStatusContract.Generated, environmentId: "env-other");
        otherEnv.Release(releasedAt, 1);
        otherEnv.Revoke(releasedAt.AddDays(1));
        dbContext.SchedulePlans.AddRange(otherOrg, otherEnv, CreatePlan("unreleased", SchedulePlanStatusContract.Generated));
        // Same plan identifier outside the requested scope must not invalidate the clean local plan.
        dbContext.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create("org-other", "env-dev", "history-103",
            "foreign", "test", "test", "foreign", null, null, null, null, releasedAt, releasedAt));
        await dbContext.SaveChangesAsync();
        var handler = new ListSchedulePlanHistoryQueryHandler(dbContext);
        var invalid = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev", 1, 50,
            SchedulePlanStatusContract.Revoked, new DateOnly(2026, 6, 2), true), CancellationToken.None);
        Assert.Equal(53, invalid.Total);
        Assert.Equal(new[] { "history-004", "history-002", "history-000" }, invalid.Items.Select(x => x.PlanId));
        Assert.All(invalid.Items, item => { Assert.True(item.IsInvalidated); Assert.Equal("latest", item.LatestInvalidationReasonCode); });
        var clean = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev", 0, 100,
            SchedulePlanStatusContract.Revoked, new DateOnly(2026, 6, 2), false), CancellationToken.None);
        Assert.Equal(52, clean.Total);
        Assert.Equal("history-103", clean.Items.First().PlanId);
        Assert.All(clean.Items, item => Assert.False(item.IsInvalidated));
        var emptyPage = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev", 5, 100), CancellationToken.None);
        Assert.Equal(106, emptyPage.Total);
        Assert.Empty(emptyPage.Items);
    }

    [Fact]
    public async Task History_orders_release_before_generation_and_reads_only_scoped_horizons()
    {
        await using var dbContext = CreateDbContext();
        var day = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var early = CreatePlan("early-release", SchedulePlanStatusContract.Generated, generatedAtUtc: day.AddHours(10));
        early.Release(day.AddDays(1), 1);
        var late = CreatePlan("late-release", SchedulePlanStatusContract.Generated, generatedAtUtc: day);
        late.Release(day.AddDays(2), 2);
        late.Revoke(day.AddDays(3));
        dbContext.SchedulePlans.AddRange(early, late,
            CreatePlan("draft-b", SchedulePlanStatusContract.Generated, generatedAtUtc: day.AddDays(5)),
            CreatePlan("draft-a", SchedulePlanStatusContract.Generated, generatedAtUtc: day.AddDays(5)),
            CreatePlan("draft-old", SchedulePlanStatusContract.Generated, generatedAtUtc: day));
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot("problem-001", 1, "org-001", "env-dev", "test", "{}", day.AddDays(-2), day.AddDays(10), day));
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot("problem-001", 1, "org-other", "env-dev", "foreign", "{}", day, day.AddHours(1), day));
        await dbContext.SaveChangesAsync();
        var handler = new ListSchedulePlanHistoryQueryHandler(dbContext);
        var page = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev"), CancellationToken.None);
        Assert.Equal(new[] { "late-release", "early-release", "draft-a", "draft-b", "draft-old" }, page.Items.Select(x => x.PlanId));
        Assert.All(page.Items, item => { Assert.Equal(day.AddDays(-2), item.HorizonStartUtc); Assert.Equal(day.AddDays(10), item.HorizonEndUtc); });
        var revoked = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev", Status: SchedulePlanStatusContract.Revoked,
            ReleasedOn: new DateOnly(2026, 6, 3)), CancellationToken.None);
        Assert.Equal(1, revoked.Total);
        Assert.Equal("late-release", Assert.Single(revoked.Items).PlanId);
    }

    [Fact]
    public async Task History_uses_utc_release_date_and_does_not_invent_missing_horizons()
    {
        await using var dbContext = CreateDbContext();
        var plan = CreatePlan("utc-boundary", SchedulePlanStatusContract.Generated);
        plan.Release(new DateTimeOffset(2026, 6, 2, 1, 0, 0, TimeSpan.FromHours(2)), 1);
        dbContext.SchedulePlans.Add(plan);
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot("problem-001", 1, "org-001", "env-other", "foreign", "{}",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();
        var handler = new ListSchedulePlanHistoryQueryHandler(dbContext);
        var firstDay = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev", ReleasedOn: new DateOnly(2026, 6, 1)), CancellationToken.None);
        var item = Assert.Single(firstDay.Items);
        Assert.Null(item.HorizonStartUtc);
        Assert.Null(item.HorizonEndUtc);
        var secondDay = await handler.Handle(new ListSchedulePlanHistoryQuery("org-001", "env-dev", ReleasedOn: new DateOnly(2026, 6, 2)), CancellationToken.None);
        Assert.Equal(0, secondDay.Total);
        Assert.Empty(secondDay.Items);
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

    private static SchedulePlan CreatePlan(string planId, SchedulePlanStatusContract status, string organizationId = "org-001", string environmentId = "env-dev", DateTimeOffset? generatedAtUtc = null)
    {
        return SchedulePlan.FromGeneratedPlan(
            organizationId,
            environmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: "problem-001",
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: status,
                GeneratedAtUtc: generatedAtUtc ?? new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
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
