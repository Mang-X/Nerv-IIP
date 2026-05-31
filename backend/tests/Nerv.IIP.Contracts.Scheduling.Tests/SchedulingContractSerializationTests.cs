using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Contracts.Scheduling.Tests;

public class SchedulingContractSerializationTests
{
    [Fact]
    public void Scheduling_problem_round_trips_contract_version_and_core_inputs()
    {
        var problem = SchedulingContractSamples.CreateShockAbsorberProblem();

        var json = JsonSerializer.Serialize(problem, SchedulingJson.Options);
        var roundTrip = JsonSerializer.Deserialize<SchedulingProblemContract>(json, SchedulingJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal(1, roundTrip!.ContractVersion);
        Assert.Equal("org-001", roundTrip.OrganizationId);
        Assert.Contains(roundTrip.Orders, x => x.OrderId == "WO-RUSH-REAR-001");
        Assert.Contains(roundTrip.Resources, x => x.ResourceId == "DEV-OIL-01");
        Assert.Contains(roundTrip.MaterialReadiness, x =>
            x.ScopeType == "order"
            && x.ScopeId == "WO-RUSH-REAR-001"
            && x.IsReady
            && x.MaterialReadyUtc == new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.Contains(roundTrip.QualityBlocks, x =>
            x.ScopeType == "resource"
            && x.ScopeId == "DEV-TEST-01"
            && x.ReasonCode == "inspection-hold"
            && x.BlockedUntilUtc == new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Schedule_plan_round_trips_assignments_conflicts_and_gantt_items()
    {
        var plan = SchedulingContractSamples.CreateExpectedShockAbsorberPlan();

        var json = JsonSerializer.Serialize(plan, SchedulingJson.Options);
        var roundTrip = JsonSerializer.Deserialize<SchedulePlanContract>(json, SchedulingJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal("aps-lite-v1", roundTrip!.AlgorithmVersion);
        Assert.NotEmpty(roundTrip.Assignments);
        Assert.NotEmpty(roundTrip.ResourceLoads);
        Assert.NotEmpty(roundTrip.GanttItems);
        Assert.Contains("\"reasonCode\":\"dueDate\"", json);
        Assert.Contains(roundTrip.Conflicts, x =>
            x.ReasonCode == ScheduleConflictReasonCodeContract.DueDate
            && x.Severity == ScheduleConflictSeverityContract.Warning
            && x.OperationId == "WO-FRONT-001-OIL");
        Assert.Contains(roundTrip.GanttItems, x =>
            x.ItemId == "gantt-conflict-001"
            && x.HasConflict
            && x.ConflictReasonCode == ScheduleConflictReasonCodeContract.DueDate);
    }
}

internal static class SchedulingContractSamples
{
    public static SchedulingProblemContract CreateShockAbsorberProblem()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var shiftEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-shock-absorber-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: shiftStart,
            HorizonEndUtc: shiftEnd.AddDays(1),
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-RUSH-REAR-001",
                    SkuCode: "FG-REAR-SHOCK",
                    Quantity: 20,
                    DueUtc: new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
                    Priority: 100,
                    IsRush: true,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-RUSH-REAR-001-OIL",
                            OperationSequence: 30,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-OIL-SEAL",
                            EligibleResourceIds: ["DEV-OIL-01"],
                            PrimaryResourceId: "DEV-OIL-01",
                            EarliestStartUtc: shiftStart,
                            DueUtc: new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
                            Priority: 100,
                            IsRush: true,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: shiftStart,
                            QualityBlockReason: null,
                            SourceReference: "MES:WO-RUSH-REAR-001")]
                ),
                new SchedulingOrderContract(
                    OrderId: "WO-FRONT-001",
                    SkuCode: "FG-FRONT-SHOCK",
                    Quantity: 40,
                    DueUtc: new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero),
                    Priority: 10,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-FRONT-001-OIL",
                            OperationSequence: 30,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-OIL-SEAL",
                            EligibleResourceIds: ["DEV-OIL-01"],
                            PrimaryResourceId: "DEV-OIL-01",
                            EarliestStartUtc: shiftStart,
                            DueUtc: new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero),
                            Priority: 10,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: shiftStart,
                            QualityBlockReason: null,
                            SourceReference: "MES:WO-FRONT-001")]
                )
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    CapabilityCodes: ["CAP-OIL-SEAL"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-DAY",
                    SortKey: "030")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-DAY",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(shiftStart, shiftEnd, "day-shift")
                    ])
            ],
            UnavailabilityWindows:
            [
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                    ReasonCode: "maintenance")
            ],
            MaterialReadiness:
            [
                new SchedulingMaterialReadinessContract(
                    ScopeType: "order",
                    ScopeId: "WO-RUSH-REAR-001",
                    MaterialReadyUtc: shiftStart,
                    IsReady: true,
                    ReasonCodes: []),
                new SchedulingMaterialReadinessContract(
                    ScopeType: "operation",
                    ScopeId: "WO-FRONT-001-OIL",
                    MaterialReadyUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    IsReady: false,
                    ReasonCodes: ["shortage"])
            ],
            QualityBlocks:
            [
                new SchedulingQualityBlockContract(
                    ScopeType: "resource",
                    ScopeId: "DEV-TEST-01",
                    ReasonCode: "inspection-hold",
                    BlockedUntilUtc: new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero))
            ],
            LockedAssignments: []);
    }

    public static SchedulePlanContract CreateExpectedShockAbsorberPlan()
    {
        var start = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        return new SchedulePlanContract(
            ContractVersion: 1,
            PlanId: "plan-preview-001",
            ProblemId: "problem-shock-absorber-001",
            ProblemFingerprint: "fingerprint-001",
            AlgorithmVersion: "aps-lite-v1",
            Status: SchedulePlanStatusContract.Preview,
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero),
            Assignments:
            [
                new ScheduleAssignmentContract(
                    AssignmentId: "assign-001",
                    OrderId: "WO-RUSH-REAR-001",
                    OperationId: "WO-RUSH-REAR-001-OIL",
                    OperationSequence: 30,
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    StartUtc: start,
                    EndUtc: end,
                    IsLocked: false,
                    ExplanationCode: "scheduled")
            ],
            ResourceLoads:
            [
                new ScheduleResourceLoadContract(
                    ResourceId: "DEV-OIL-01",
                    WindowStartUtc: start.Date,
                    WindowEndUtc: start.Date.AddDays(1),
                    AssignedMinutes: 60,
                    AvailableMinutes: 360,
                    Utilization: 0.1667m)
            ],
            Conflicts:
            [
                new ScheduleConflictContract(
                    ConflictId: "conflict-001",
                    ReasonCode: ScheduleConflictReasonCodeContract.DueDate,
                    Severity: ScheduleConflictSeverityContract.Warning,
                    OrderId: "WO-FRONT-001",
                    OperationId: "WO-FRONT-001-OIL",
                    ResourceId: "DEV-OIL-01",
                    Message: "Assignment finishes after due date.")
            ],
            UnscheduledOperations: [],
            ChangeSummary:
            [
                new ScheduleChangeContract(
                    OrderId: "WO-RUSH-REAR-001",
                    OperationId: "WO-RUSH-REAR-001-OIL",
                    ChangeType: ScheduleChangeTypeContract.Added,
                    Message: "Scheduled by APS lite.")
            ],
            GanttItems:
            [
                new GanttScheduleItemContract(
                    ItemId: "gantt-001",
                    OrderId: "WO-RUSH-REAR-001",
                    OperationId: "WO-RUSH-REAR-001-OIL",
                    OperationSequence: 30,
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    StartUtc: start,
                    EndUtc: end,
                    Status: SchedulePlanStatusContract.Preview,
                    HasConflict: false,
                    ConflictReasonCode: null),
                new GanttScheduleItemContract(
                    ItemId: "gantt-conflict-001",
                    OrderId: "WO-FRONT-001",
                    OperationId: "WO-FRONT-001-OIL",
                    OperationSequence: 30,
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    StartUtc: end,
                    EndUtc: end.AddHours(1),
                    Status: SchedulePlanStatusContract.Preview,
                    HasConflict: true,
                    ConflictReasonCode: ScheduleConflictReasonCodeContract.DueDate)
            ]);
    }
}
