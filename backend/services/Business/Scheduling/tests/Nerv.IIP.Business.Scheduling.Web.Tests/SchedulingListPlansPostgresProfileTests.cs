using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// Real-provider coverage for the DB-side bounded latest-invalidation projection (GROUP BY plan +
// ordered-First over the strict total order). SQLite cannot host ListSchedulePlansQueryHandler at all
// (its plans query ORDER BYs GeneratedAtUtc, a DateTimeOffset that SQLite refuses to sort), so both the
// translation and the exact-timestamp-tie determinism are verified against Postgres. Gated on
// NERV_IIP_TEST_POSTGRES like the other *PostgresProfileTests; skipped (returns) when no Postgres is configured.
public sealed class SchedulingListPlansPostgresProfileTests
{
    [Fact]
    public async Task Postgres_list_marks_invalidated_plan_with_latest_reason()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var database = await SchedulingTemporaryDatabase.CreateAsync(connectionString);
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlan("plan-clean"));
        dbContext.SchedulePlans.Add(CreatePlan("plan-invalid"));
        // Older material-readiness invalidation, then a newer equipment invalidation for the same plan.
        dbContext.SchedulePlanInvalidations.Add(CreateInvalidation(
            "plan-invalid",
            SchedulingPlanInvalidationReasons.MaterialReadinessChanged,
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 0, 5, TimeSpan.Zero)));
        dbContext.SchedulePlanInvalidations.Add(CreateInvalidation(
            "plan-invalid",
            SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 11, 30, 5, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var handler = new ListSchedulePlansQueryHandler(dbContext);
        var results = await handler.Handle(
            new ListSchedulePlansQuery("org-001", "env-dev"),
            CancellationToken.None);

        var clean = Assert.Single(results, x => x.PlanId == "plan-clean");
        Assert.False(clean.IsInvalidated);

        var invalid = Assert.Single(results, x => x.PlanId == "plan-invalid");
        Assert.True(invalid.IsInvalidated);
        Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalid.LatestInvalidationReasonCode);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero), invalid.LatestInvalidatedAtUtc);
    }

    [Fact]
    public async Task Postgres_list_breaks_exact_timestamp_ties_deterministically_by_id()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var database = await SchedulingTemporaryDatabase.CreateAsync(connectionString);
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        dbContext.SchedulePlans.Add(CreatePlan("plan-tie"));
        // Two invalidations with identical RecordedAtUtc AND OccurredAtUtc: the timestamps tie, so only the
        // (SourceEventType, SourceEventId) tail of the strict total order decides. Same SourceEventType, so the
        // greater SourceEventId ("evt-tie-b" > "evt-tie-a" in every collation) must win — exactly one row, and the
        // same one on every run.
        var tieRecordedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var tieOccurredAtUtc = new DateTimeOffset(2026, 6, 1, 11, 59, 0, TimeSpan.Zero);
        dbContext.SchedulePlanInvalidations.Add(CreateInvalidation("plan-tie", SchedulingPlanInvalidationReasons.MaterialReadinessChanged, tieOccurredAtUtc, tieRecordedAtUtc, sourceEventId: "evt-tie-a"));
        dbContext.SchedulePlanInvalidations.Add(CreateInvalidation("plan-tie", SchedulingPlanInvalidationReasons.EquipmentUnavailable, tieOccurredAtUtc, tieRecordedAtUtc, sourceEventId: "evt-tie-b"));
        await dbContext.SaveChangesAsync();

        var handler = new ListSchedulePlansQueryHandler(dbContext);
        // Run twice: exactly one deterministic row (the greater SourceEventId) wins every time.
        for (var run = 0; run < 2; run++)
        {
            var results = await handler.Handle(new ListSchedulePlansQuery("org-001", "env-dev"), CancellationToken.None);
            var tie = Assert.Single(results, x => x.PlanId == "plan-tie");
            Assert.True(tie.IsInvalidated);
            Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, tie.LatestInvalidationReasonCode);
        }
    }

    private static SchedulePlanInvalidation CreateInvalidation(
        string planId,
        string reasonCode,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string? sourceEventId = null)
    {
        return SchedulePlanInvalidation.Create(
            "org-001",
            "env-dev",
            planId,
            sourceEventId: sourceEventId ?? $"evt-{reasonCode}-{recordedAtUtc.Ticks}",
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

    private static SchedulePlan CreatePlan(string planId)
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
                Status: SchedulePlanStatusContract.Generated,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(1, 0, 60, 60, 0, 0, 1m, 0m),
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
}
