using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public static class ShockAbsorberSchedulingFixture
{
    public static SchedulingProblemContract CreateProblem()
    {
        var day1Start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var day1End = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);
        var day2Start = new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero);
        var day2End = new DateTimeOffset(2026, 6, 2, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-shock-absorber-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: day1Start,
            HorizonEndUtc: day2End,
            Orders:
            [
                CreateOrder(
                    orderId: "WO-RUSH-REAR-001",
                    skuCode: "FG-REAR-SHOCK",
                    dueUtc: new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero),
                    priority: 100,
                    isRush: true,
                    earliestStartUtc: day1Start),
                CreateOrder(
                    orderId: "WO-FRONT-001",
                    skuCode: "FG-FRONT-SHOCK",
                    dueUtc: new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero),
                    priority: 10,
                    isRush: false,
                    earliestStartUtc: day1Start)
            ],
            Resources:
            [
                new SchedulingResourceContract("DEV-WELD-01", "WC-TUBE-WELD", ["CAP-TUBE-WELD"], 1, "CAL-DAY", "010"),
                new SchedulingResourceContract("DEV-ROD-01", "WC-ROD-ASSEMBLY", ["CAP-ROD-ASSEMBLY"], 1, "CAL-DAY", "020"),
                new SchedulingResourceContract("DEV-OIL-01", "WC-OIL-SEAL", ["CAP-OIL-SEAL"], 1, "CAL-DAY", "030"),
                new SchedulingResourceContract("DEV-TEST-01", "WC-DAMPING-TEST", ["CAP-DAMPING-TEST"], 1, "CAL-DAY", "040")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    "CAL-DAY",
                    [
                        new SchedulingTimeWindowContract(day1Start, day1End, "day-shift"),
                        new SchedulingTimeWindowContract(day2Start, day2End, "day-shift")
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
                    MaterialReadyUtc: day1Start,
                    IsReady: true,
                    ReasonCodes: []),
                new SchedulingMaterialReadinessContract(
                    ScopeType: "order",
                    ScopeId: "WO-FRONT-001",
                    MaterialReadyUtc: day1Start,
                    IsReady: true,
                    ReasonCodes: [])
            ],
            QualityBlocks:
            [
                new SchedulingQualityBlockContract(
                    ScopeType: "resource",
                    ScopeId: "DEV-TEST-01",
                    ReasonCode: "inspection-hold",
                    BlockedUntilUtc: day2Start)
            ],
            LockedAssignments: []);
    }

    private static SchedulingOrderContract CreateOrder(
        string orderId,
        string skuCode,
        DateTimeOffset dueUtc,
        int priority,
        bool isRush,
        DateTimeOffset earliestStartUtc)
    {
        return new SchedulingOrderContract(
            OrderId: orderId,
            SkuCode: skuCode,
            Quantity: isRush ? 20 : 40,
            DueUtc: dueUtc,
            Priority: priority,
            IsRush: isRush,
            Operations:
            [
                CreateOperation(orderId, "WELD", 10, [], 60, "CAP-TUBE-WELD", "DEV-WELD-01", "WC-TUBE-WELD", dueUtc, priority, isRush, earliestStartUtc),
                CreateOperation(orderId, "ROD", 20, [$"{orderId}-WELD"], 60, "CAP-ROD-ASSEMBLY", "DEV-ROD-01", "WC-ROD-ASSEMBLY", dueUtc, priority, isRush, earliestStartUtc),
                CreateOperation(orderId, "OIL", 30, [$"{orderId}-ROD"], 90, "CAP-OIL-SEAL", "DEV-OIL-01", "WC-OIL-SEAL", dueUtc, priority, isRush, earliestStartUtc),
                CreateOperation(orderId, "TEST", 40, [$"{orderId}-OIL"], 60, "CAP-DAMPING-TEST", "DEV-TEST-01", "WC-DAMPING-TEST", dueUtc, priority, isRush, earliestStartUtc)
            ]);
    }

    private static SchedulingOperationContract CreateOperation(
        string orderId,
        string operationCode,
        int sequence,
        IReadOnlyCollection<string> predecessorOperationIds,
        int durationMinutes,
        string capabilityCode,
        string resourceId,
        string workCenterId,
        DateTimeOffset dueUtc,
        int priority,
        bool isRush,
        DateTimeOffset earliestStartUtc)
    {
        return new SchedulingOperationContract(
            OperationId: $"{orderId}-{operationCode}",
            OperationSequence: sequence,
            PredecessorOperationIds: predecessorOperationIds,
            DurationMinutes: durationMinutes,
            RequiredCapabilityCode: capabilityCode,
            EligibleResourceIds: [resourceId],
            PrimaryResourceId: resourceId,
            EarliestStartUtc: earliestStartUtc,
            DueUtc: dueUtc,
            Priority: priority,
            IsRush: isRush,
            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
            MaterialReadyUtc: earliestStartUtc,
            QualityBlockReason: null,
            SourceReference: $"{workCenterId}:{operationCode}");
    }
}
