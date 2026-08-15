using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
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

            plan.ReplaceGeneratedPlan(SchedulePlanContractMapper.ToDomainSnapshot(CreateReplacementContract()));
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
        }
    }

    [Fact]
    public async Task Generated_plan_risks_survive_persistence_round_trip()
    {
        var problem = CreateRiskRoundTripProblem();
        var generated = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(
                problem,
                "plan-risk-round-trip-001",
                problem.HorizonStartUtc),
            SchedulePlanStatusContract.Generated);

        Assert.Single(generated.MaterialRisks ?? []);
        Assert.Single(generated.EquipmentRisks ?? []);
        Assert.Equal(1, generated.Metrics.MaterialRiskOperationCount);
        Assert.Equal(1, generated.Metrics.EquipmentRiskOperationCount);
        Assert.Contains(generated.GanttItems, x => x.HasMaterialRisk && x.HasEquipmentRisk);

        await using var provider = CreateInMemoryProvider();
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
                problem.OrganizationId,
                problem.EnvironmentId,
                SchedulePlanContractMapper.ToDomainSnapshot(generated)));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        SchedulePlanContract reloaded;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await dbContext.SchedulePlans
                .AsNoTracking()
                .Include(x => x.Assignments)
                .Include(x => x.ResourceLoads)
                .Include(x => x.Conflicts)
                .Include(x => x.UnscheduledOperations)
                .SingleAsync(x => x.PlanId == generated.PlanId, CancellationToken.None);

            reloaded = SchedulePlanContractMapper.ToContract(persisted);
        }

        Assert.Equal(
            JsonSerializer.Serialize(generated.MaterialRisks, SchedulingJson.Options),
            JsonSerializer.Serialize(reloaded.MaterialRisks, SchedulingJson.Options));
        Assert.Equal(
            JsonSerializer.Serialize(generated.EquipmentRisks, SchedulingJson.Options),
            JsonSerializer.Serialize(reloaded.EquipmentRisks, SchedulingJson.Options));
        Assert.Equal(generated.Metrics.MaterialRiskOperationCount, reloaded.Metrics.MaterialRiskOperationCount);
        Assert.Equal(generated.Metrics.EquipmentRiskOperationCount, reloaded.Metrics.EquipmentRiskOperationCount);
        Assert.Equal(
            generated.GanttItems.Select(x => (x.OperationId, x.HasMaterialRisk, x.HasEquipmentRisk)),
            reloaded.GanttItems.Select(x => (x.OperationId, x.HasMaterialRisk, x.HasEquipmentRisk)));
    }

    /// <summary>
    /// #1409：设备不可用窗口（甘特上的维护/停机遮罩带）必须随方案落库。
    ///
    /// 它以前只在生成响应里出现，重读时靠 <c>ProblemJson</c> 投影——但问题快照存的是
    /// 「设备可用性适配之前」的原始输入，其 <c>UnavailabilityWindows</c> 恒为空，
    /// 于是 POST 有 5~7 条遮罩、GET 变 0 条：刚生成时甘特上有遮罩，刷新就没了。
    ///
    /// 这里故意不传 problem 重读（模拟问题快照缺失的最坏情况），遮罩仍必须在。
    /// </summary>
    [Fact]
    public async Task Generated_plan_block_windows_survive_persistence_round_trip()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var generated = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(
                problem,
                "plan-block-window-round-trip-001",
                problem.HorizonStartUtc),
            SchedulePlanStatusContract.Generated);

        Assert.NotEmpty(generated.BlockWindows ?? []);

        await using var provider = CreateInMemoryProvider();
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
                problem.OrganizationId,
                problem.EnvironmentId,
                SchedulePlanContractMapper.ToDomainSnapshot(generated)));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        SchedulePlanContract reloaded;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await dbContext.SchedulePlans
                .AsNoTracking()
                .Include(x => x.Assignments)
                .Include(x => x.ResourceLoads)
                .Include(x => x.Conflicts)
                .Include(x => x.UnscheduledOperations)
                .SingleAsync(x => x.PlanId == generated.PlanId, CancellationToken.None);

            reloaded = SchedulePlanContractMapper.ToContract(persisted);
        }

        Assert.Equal(
            JsonSerializer.Serialize(generated.BlockWindows, SchedulingJson.Options),
            JsonSerializer.Serialize(reloaded.BlockWindows, SchedulingJson.Options));
    }

    private static SchedulingProblemContract CreateRiskRoundTripProblem()
    {
        var horizonStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var horizonEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-risk-round-trip-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            HorizonStartUtc: horizonStart,
            HorizonEndUtc: horizonEnd,
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-RISK-001",
                    SkuCode: "FG-RISK",
                    Quantity: 1,
                    DueUtc: horizonEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-RISK-001-OP10",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-RISK",
                            EligibleResourceIds: ["DEV-RISK-01"],
                            PrimaryResourceId: "DEV-RISK-01",
                            EarliestStartUtc: horizonStart,
                            DueUtc: horizonEnd,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:RISK-ROUND-TRIP")
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-RISK-01",
                    WorkCenterId: "WC-RISK",
                    CapabilityCodes: ["CAP-RISK"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-RISK",
                    SortKey: "001")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-RISK",
                    ShiftWindows: [new SchedulingTimeWindowContract(horizonStart, horizonEnd, "day-shift")])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness:
            [
                new SchedulingMaterialReadinessContract(
                    ScopeType: "order",
                    ScopeId: "WO-RISK-001",
                    MaterialReadyUtc: null,
                    IsReady: false,
                    ReasonCodes: ["material-shortage"],
                    Shortages: [new SchedulingMaterialShortageContract("RM-RISK-01", "LOT-RISK-01", 10m, 4m, 6m)])
            ],
            QualityBlocks: [],
            LockedAssignments: [],
            EquipmentDataRisks:
            [
                new SchedulingEquipmentDataRiskContract(
                    ResourceId: "DEV-RISK-01",
                    WorkCenterId: "WC-RISK",
                    ReasonCode: "source-stale",
                    StartUtc: horizonStart,
                    EndUtc: horizonEnd,
                    SourceReferenceLabel: "TEST:RISK-ROUND-TRIP")
            ]);
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
        return SchedulePlan.FromGeneratedPlan("org-001", "env-dev", SchedulePlanContractMapper.ToDomainSnapshot(CreateContract(
            assignmentId: "assign-old",
            operationId: "op-old",
            resourceId: "res-old",
            conflictId: "conflict-old",
            unscheduledWorkOrderId: "wo-unscheduled-old",
            unscheduledOperationId: "op-unscheduled-old",
            assignedMinutes: 60)));
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
