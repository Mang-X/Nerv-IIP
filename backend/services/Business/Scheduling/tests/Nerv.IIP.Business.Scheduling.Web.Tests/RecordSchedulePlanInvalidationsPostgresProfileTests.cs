using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// Real-provider guard for RecordSchedulePlanInvalidationsCommandHandler.QueryPlans. The plan filter must
// be a SQL-translatable predicate: a custom method call (IsInvalidatableStatus) inside Where throws
// "could not be translated" on Postgres, which meant the whole AssetUnavailable -> plan invalidation ->
// MES marking chain silently failed on a real database while EF InMemory (client evaluation) kept the
// unit tests green. Gated on NERV_IIP_TEST_POSTGRES like the other *PostgresProfileTests.
public sealed class RecordSchedulePlanInvalidationsPostgresProfileTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Postgres_records_invalidation_for_a_generated_plan_matched_by_resource()
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

        dbContext.SchedulePlans.Add(CreatePlanWithAssignment("plan-a", resourceId: "ASSET-CNC-01"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordSchedulePlanInvalidationsCommandHandler(dbContext, new FixedTimeProvider(FixedNow));
        var response = await handler.Handle(
            new RecordSchedulePlanInvalidationsCommand(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                SourceEventId: "evt-1",
                SourceEventType: "maintenance.AssetUnavailable",
                SourceService: "maintenance",
                OccurredAtUtc: FixedNow,
                ReasonCode: SchedulingPlanInvalidationReasons.EquipmentUnavailable,
                Scope: SchedulePlanInvalidationScope.Resource,
                ScopeValue: "ASSET-CNC-01",
                AffectedWorkOrderId: null,
                AffectedSkuCode: null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, response.MatchedPlanCount);
        Assert.Equal(1, response.RecordedInvalidationCount);
        var invalidation = await dbContext.SchedulePlanInvalidations.SingleAsync(x => x.PlanId == "plan-a");
        Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalidation.ReasonCode);
        Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);

        await dbContext.Database.EnsureDeletedAsync();
    }

    private static SchedulePlan CreatePlanWithAssignment(string planId, string resourceId)
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
                        ResourceId: resourceId,
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
