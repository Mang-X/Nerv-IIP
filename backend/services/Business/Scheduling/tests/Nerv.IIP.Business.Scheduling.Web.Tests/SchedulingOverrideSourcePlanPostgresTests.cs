using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class SchedulingOverrideSourcePlanPostgresTests
{
    [SchedulingPostgresFact]
    public async Task Migration_preserves_legacy_override_and_manual_replacements_track_source_plan()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrations = db.Database.GetMigrations().ToArray();
        var sourceMigration = Array.FindIndex(migrations, x => x.EndsWith("_AddSchedulingOverrideSourcePlan", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(migrations[sourceMigration - 1]);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var legacyId = Guid.NewGuid();
        var now = problem.HorizonStartUtc;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO scheduling.schedule_operation_overrides
            (id, organization_id, environment_id, work_order_id, operation_id, operation_sequence,
             resource_id, work_center_id, start_utc, end_utc, lock_reason_code, source_type,
             actor, source_occurred_at_utc, updated_at_utc, is_active)
            VALUES ({legacyId}, 'org-legacy', 'env-legacy', 'wo-legacy', 'op-legacy', 10,
                    'dev-legacy', 'wc-legacy', {now}, {now.AddHours(1)}, 'manual-override',
                    'scheduling-api', 'user:legacy', {now}, {now}, true)
            """);
        await db.Database.MigrateAsync();
        var legacy = await db.ScheduleOperationOverrides.AsNoTracking().SingleAsync();
        Assert.Null(legacy.SourcePlanId);
        Assert.Equal(("org-legacy", "env-legacy", "wo-legacy", "op-legacy", 10,
            "dev-legacy", "wc-legacy", now, now.AddHours(1), "manual-override", "scheduling-api",
            "user:legacy", now, now, true),
            (legacy.OrganizationId, legacy.EnvironmentId, legacy.WorkOrderId, legacy.OperationId,
            legacy.OperationSequence, legacy.ResourceId, legacy.WorkCenterId, legacy.StartUtc,
            legacy.EndUtc, legacy.LockReasonCode, legacy.SourceType, legacy.Actor,
            legacy.SourceOccurredAtUtc, legacy.UpdatedAtUtc, legacy.IsActive));

        var scheduler = new FiniteCapacityScheduler();
        var firstPlan = scheduler.Schedule(problem, "plan-source-first", now);
        var secondProblem = problem with { ProblemId = problem.ProblemId + "-second" };
        var secondPlan = scheduler.Schedule(secondProblem, "plan-source-second", now);
        foreach (var pair in new[] { (Problem: problem, Plan: firstPlan), (Problem: secondProblem, Plan: secondPlan) })
        {
            db.ScheduleProblems.Add(new ScheduleProblemSnapshot(pair.Problem.ProblemId, 1,
                problem.OrganizationId, problem.EnvironmentId, pair.Plan.ProblemFingerprint,
                System.Text.Json.JsonSerializer.Serialize(pair.Problem, SchedulingJson.Options),
                problem.HorizonStartUtc, problem.HorizonEndUtc, now));
            db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
                SchedulePlanContractMapper.ToDomainSnapshot(pair.Plan)));
        }
        await db.SaveChangesAsync();
        var assignment = firstPlan.Assignments.First();
        var handler = new UpsertScheduleOperationOverrideCommandHandler(db,
            new FixedTimeProvider(now), new ContextAccessor());
        var request = new UpsertScheduleOperationOverrideCommand(problem.OrganizationId, problem.EnvironmentId,
            firstPlan.PlanId, assignment.OperationId, assignment.ResourceId, assignment.StartUtc, assignment.EndUtc);
        await handler.Handle(request, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fact = await db.ScheduleOperationOverrides.SingleAsync(x => x.OperationId == assignment.OperationId);
        Assert.Equal(firstPlan.PlanId, fact.SourcePlanId);
        var inherited = await new SchedulingOperationOverrideOverlay(db).ApplyAsync(secondProblem, CancellationToken.None);
        var locked = Assert.Single(inherited.LockedAssignments, x => x.OperationId == assignment.OperationId);
        Assert.Equal((fact.ResourceId, fact.StartUtc, fact.EndUtc), (locked.ResourceId, locked.StartUtc, locked.EndUtc));
        await handler.Handle(request with { PlanId = secondPlan.PlanId, StartUtc = assignment.StartUtc.AddMinutes(5), EndUtc = assignment.EndUtc.AddMinutes(5) }, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        fact = await db.ScheduleOperationOverrides.SingleAsync(x => x.OperationId == assignment.OperationId);
        Assert.Equal(secondPlan.PlanId, fact.SourcePlanId);
        Assert.False(fact.TryApplyMesDispatch(fact.ResourceId, fact.WorkCenterId, fact.StartUtc, fact.EndUtc,
            "evt-stale", "user:mes", 1, now.AddMinutes(-1), now));
        Assert.Equal(secondPlan.PlanId, fact.SourcePlanId);
        Assert.True(fact.TryApplyMesDispatch(fact.ResourceId, fact.WorkCenterId, fact.StartUtc, fact.EndUtc,
            "evt-new", "user:mes", 2, now.AddMinutes(1), now.AddMinutes(1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        fact = await db.ScheduleOperationOverrides.SingleAsync(x => x.OperationId == assignment.OperationId);
        Assert.Null(fact.SourcePlanId);
        Assert.Equal("mes-dispatch", fact.SourceType);
        Assert.Equal(2, fact.SourceRevision);
        Assert.Equal(2, await db.ScheduleOperationOverrides.CountAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext() => new("corr", "cause", "user:planner");
    }
}
