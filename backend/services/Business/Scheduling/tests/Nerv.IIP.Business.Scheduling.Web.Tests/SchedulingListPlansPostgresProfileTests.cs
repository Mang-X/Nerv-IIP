using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// Real-provider coverage for the bounded latest-invalidation anti-join. SQLite cannot host
// ListSchedulePlansQueryHandler at all (its plans query ORDER BYs GeneratedAtUtc, a DateTimeOffset that
// SQLite refuses to sort), so translation is verified against Postgres. Gated on NERV_IIP_TEST_POSTGRES
// like the other *PostgresProfileTests; skipped (returns) when no Postgres is configured.
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

        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
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

        await dbContext.Database.EnsureDeletedAsync();
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
