using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Infrastructure.Repositories;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingPersistenceTests
{
    [Fact]
    public void Order_urgency_persistence_is_scoped_and_idempotent()
    {
        using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var priority = dbContext.Model.FindEntityType(typeof(OrderUrgencyBusinessPriority))
            ?? throw new InvalidOperationException("Order urgency business priority metadata was not found.");
        var snapshot = dbContext.Model.FindEntityType(typeof(OrderUrgencySnapshot))
            ?? throw new InvalidOperationException("Order urgency snapshot metadata was not found.");

        Assert.Contains(priority.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                ["OrganizationId", "EnvironmentId", "OrderId"]));
        Assert.Contains(snapshot.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                ["OrganizationId", "EnvironmentId", "OrderId", "ModelVersion", "InputFingerprint", "BusinessPriorityRevision", "CalculationBucketUtc"]));
    }

    [Fact]
    public void Schedule_problem_snapshot_uniqueness_is_scoped_to_business_context()
    {
        using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entityType = dbContext.Model.FindEntityType(typeof(ScheduleProblemSnapshot))
            ?? throw new InvalidOperationException("ScheduleProblemSnapshot entity metadata was not found.");
        var scopedIndex = entityType.GetIndexes().SingleOrDefault(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                ["OrganizationId", "EnvironmentId", "ProblemId"]));

        Assert.NotNull(scopedIndex);
    }

    [Fact]
    public async Task Base_problem_and_effective_engine_input_snapshots_round_trip_independently()
    {
        await using var provider = CreateInMemoryProvider();
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
                problemId: "problem-dual-input",
                contractVersion: 1,
                organizationId: "org-001",
                environmentId: "env-dev",
                problemFingerprint: "base-fingerprint",
                problemJson: """{"problemId":"base"}""",
                horizonStartUtc: new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                horizonEndUtc: new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
                capturedAtUtc: new DateTimeOffset(2026, 7, 26, 23, 0, 0, TimeSpan.Zero),
                engineInputFingerprint: "effective-fingerprint",
                engineInputJson: """{"problemId":"effective"}"""));
            await dbContext.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await dbContext.ScheduleProblems.AsNoTracking()
                .SingleAsync(x => x.ProblemId == "problem-dual-input");

            Assert.Equal("base-fingerprint", persisted.ProblemFingerprint);
            Assert.Equal("""{"problemId":"base"}""", persisted.ProblemJson);
            Assert.Equal("effective-fingerprint", persisted.EngineInputFingerprint);
            Assert.Equal("""{"problemId":"effective"}""", persisted.EngineInputJson);
        }
    }

    [Fact]
    public async Task Repository_detail_loading_path_replaces_persisted_child_facts()
    {
        var cancellationToken = CancellationToken.None;
        await using var provider = CreateInMemoryProvider();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.SchedulePlans.Add(CreatePlan());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var repository = new SchedulePlanRepository(dbContext);
            Assert.Null(await repository.GetByPlanIdWithDetailsAsync("plan-001", "org-other", "env-dev", cancellationToken));
            Assert.Null(await repository.GetByPlanIdWithDetailsAsync("plan-001", "org-001", "env-other", cancellationToken));

            var plan = await repository.GetByPlanIdWithDetailsAsync("plan-001", "org-001", "env-dev", cancellationToken);
            Assert.NotNull(plan);

            plan.ReplaceGeneratedPlan(
                SchedulePlanContractMapper.ToDomainSnapshot(CreateReplacementContract()),
                CreateTrace(
                    engineId: "replacement-engine",
                    ruleProviderId: "replacement-rules",
                    ruleProfileId: "replacement-profile",
                    ruleProfileVersion: "v2"));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await dbContext.SchedulePlans
                .Include(x => x.Assignments)
                .Include(x => x.ResourceLoads)
                .Include(x => x.Conflicts)
                .Include(x => x.UnscheduledOperations)
                .SingleAsync(x => x.PlanId == "plan-001", cancellationToken);

            Assert.Single(persisted.Assignments);
            Assert.Contains(persisted.Assignments, x => x.AssignmentId == "assign-new" && x.OperationId == "op-new");
            Assert.DoesNotContain(persisted.Assignments, x => x.AssignmentId == "assign-old");

            Assert.Single(persisted.ResourceLoads);
            Assert.Contains(persisted.ResourceLoads, x => x.ResourceId == "res-new" && x.AssignedMinutes == 180);
            Assert.DoesNotContain(persisted.ResourceLoads, x => x.ResourceId == "res-old");

            Assert.Single(persisted.Conflicts);
            Assert.Contains(persisted.Conflicts, x => x.ConflictPublicId == "conflict-new");
            Assert.DoesNotContain(persisted.Conflicts, x => x.ConflictPublicId == "conflict-old");

            Assert.Single(persisted.UnscheduledOperations);
            Assert.Contains(persisted.UnscheduledOperations, x => x.WorkOrderId == "wo-new" && x.OperationId == "op-unscheduled-new");
            Assert.DoesNotContain(persisted.UnscheduledOperations, x => x.WorkOrderId == "wo-unscheduled-old");
            Assert.Equal("replacement-engine", persisted.EngineId);
            Assert.Equal("aps-lite-v1", persisted.AlgorithmVersion);
            Assert.Equal("replacement-rules", persisted.RuleProviderId);
            Assert.Equal("replacement-profile", persisted.RuleProfileId);
            Assert.Equal("v2", persisted.RuleProfileVersion);
        }
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"scheduling-persistence-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static SchedulePlan CreatePlan()
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(CreateContract(
                assignmentId: "assign-old",
                operationId: "op-old",
                resourceId: "res-old",
                conflictId: "conflict-old",
                unscheduledWorkOrderId: "wo-unscheduled-old",
                unscheduledOperationId: "op-unscheduled-old",
                assignedMinutes: 60)),
            CreateTrace("finite-capacity", "built-in", "adr-0014-default", "v1"));
    }

    private static SchedulePlanExecutionTraceSnapshot CreateTrace(
        string engineId,
        string ruleProviderId,
        string ruleProfileId,
        string ruleProfileVersion)
    {
        return new SchedulePlanExecutionTraceSnapshot(
            EngineId: engineId,
            EngineVersion: "aps-lite-v1",
            RuleProviderId: ruleProviderId,
            RuleProfileId: ruleProfileId,
            RuleProfileVersion: ruleProfileVersion,
            ConstraintSourcesJson: """{"schemaVersion":1,"sources":[]}""",
            TraceSchemaVersion: 1,
            ReplayStatus: SchedulingReplayStatuses.Available);
    }

    private static SchedulePlanContract CreateReplacementContract()
    {
        return CreateContract(
            assignmentId: "assign-new",
            operationId: "op-new",
            resourceId: "res-new",
            conflictId: "conflict-new",
            unscheduledWorkOrderId: "wo-new",
            unscheduledOperationId: "op-unscheduled-new",
            assignedMinutes: 180);
    }

    private static SchedulePlanContract CreateContract(
        string assignmentId,
        string operationId,
        string resourceId,
        string conflictId,
        string unscheduledWorkOrderId,
        string unscheduledOperationId,
        int assignedMinutes)
    {
        return new SchedulePlanContract(
            ContractVersion: 1,
            PlanId: "plan-001",
            ProblemId: "problem-001",
            ProblemFingerprint: $"fingerprint-{assignmentId}",
            AlgorithmVersion: "aps-lite-v1",
            Status: SchedulePlanStatusContract.Generated,
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            Metrics: new SchedulePlanMetricsContract(
                ScheduledOperationCount: 1,
                UnscheduledOperationCount: 1,
                AssignedMinutes: assignedMinutes,
                MakespanMinutes: 60,
                TotalTardinessMinutes: 0,
                LateOperationCount: 0,
                OnTimeRate: 1m,
                AverageResourceUtilization: Math.Round(assignedMinutes / 480m, 4)),
            Assignments:
            [
                new ScheduleAssignmentContract(
                    AssignmentId: assignmentId,
                    OrderId: "wo-001",
                    OperationId: operationId,
                    OperationSequence: 10,
                    ResourceId: resourceId,
                    WorkCenterId: "wc-001",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    IsLocked: false,
                    ExplanationCode: "scheduled")
            ],
            ResourceLoads:
            [
                new ScheduleResourceLoadContract(
                    ResourceId: resourceId,
                    WindowStartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    WindowEndUtc: new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero),
                    AssignedMinutes: assignedMinutes,
                    AvailableMinutes: 480,
                    Utilization: 0.375m)
            ],
            Conflicts:
            [
                new ScheduleConflictContract(
                    ConflictId: conflictId,
                    ReasonCode: ScheduleConflictReasonCodeContract.Material,
                    Severity: ScheduleConflictSeverityContract.Warning,
                    OrderId: "wo-001",
                    OperationId: operationId,
                    ResourceId: resourceId,
                    Message: "material unavailable")
            ],
            UnscheduledOperations:
            [
                new UnscheduledOperationContract(
                    OrderId: unscheduledWorkOrderId,
                    OperationId: unscheduledOperationId,
                    ReasonCode: ScheduleConflictReasonCodeContract.NoEligibleResource,
                    Message: "no eligible resource")
            ],
            ChangeSummary: [],
            GanttItems: []);
    }
}
