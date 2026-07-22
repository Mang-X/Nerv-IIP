using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed record SchedulingScaleProfile(string Name, int OrderCount)
{
    public const int OperationsPerOrder = 4;
    public const int ResourceCount = 24;

    public static IReadOnlyList<SchedulingScaleProfile> All { get; } =
    [
        new("demo", 100),
        new("medium", 500),
        new("stress", 1000),
    ];
}

public static class SchedulingScaleProblemFactory
{
    private static readonly DateTimeOffset BenchmarkDateUtc = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HorizonStartUtc = BenchmarkDateUtc.AddHours(8);
    private static readonly DateTimeOffset HorizonEndUtc = BenchmarkDateUtc.AddDays(45).AddHours(20);

    private static readonly OperationStage[] Stages =
    [
        new("WELD", 10, 45, "CAP-TUBE-WELD", "WC-TUBE-WELD", "WELD"),
        new("ROD", 20, 40, "CAP-ROD-ASSEMBLY", "WC-ROD-ASSEMBLY", "ROD"),
        new("OIL", 30, 60, "CAP-OIL-SEAL", "WC-OIL-SEAL", "OIL"),
        new("TEST", 40, 35, "CAP-DAMPING-TEST", "WC-DAMPING-TEST", "TEST"),
    ];

    public static SchedulingProblemContract Create(SchedulingScaleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var orders = Enumerable.Range(1, profile.OrderCount)
            .Select(CreateOrder)
            .ToArray();
        var materialReadiness = Enumerable.Range(1, profile.OrderCount)
            .Where(IsMaterialBlocked)
            .Select(index => new SchedulingMaterialReadinessContract(
                ScopeType: "order",
                ScopeId: OrderId(index),
                MaterialReadyUtc: null,
                IsReady: false,
                ReasonCodes: ["component-shortage"]))
            .ToArray();
        var qualityBlocks = Enumerable.Range(1, profile.OrderCount)
            .Where(IsQualityBlocked)
            .Select(index => new SchedulingQualityBlockContract(
                ScopeType: "operation",
                ScopeId: OperationId(index, "OIL"),
                ReasonCode: "quality-hold",
                BlockedUntilUtc: null))
            .ToArray();

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: $"aps-scale-{profile.Name}",
            OrganizationId: "org-aps-scale",
            EnvironmentId: "benchmark",
            HorizonStartUtc: HorizonStartUtc,
            HorizonEndUtc: HorizonEndUtc,
            Orders: orders,
            Resources: CreateResources(),
            Calendars: [CreateCalendar()],
            UnavailabilityWindows: CreateUnavailabilityWindows(),
            MaterialReadiness: materialReadiness,
            QualityBlocks: qualityBlocks,
            LockedAssignments: []);
    }

    private static SchedulingOrderContract CreateOrder(int index)
    {
        var orderId = OrderId(index);
        var dueUtc = BenchmarkDateUtc.AddDays(5 + (index % 35)).AddHours(18);
        var isRush = index % 29 == 0;
        var priority = isRush ? 100 : 1 + (index % 9);
        var operations = Stages.Select((stage, stageIndex) =>
        {
            var predecessorIds = stageIndex == 0
                ? Array.Empty<string>()
                : [OperationId(index, Stages[stageIndex - 1].Code)];
            var eligibleResources = IsResourceBlocked(index) && stageIndex == 0
                ? Array.Empty<string>()
                : Enumerable.Range(1, 6)
                    .Select(resourceIndex => ResourceId(stage.ResourcePrefix, resourceIndex))
                    .ToArray();

            return new SchedulingOperationContract(
                OperationId: OperationId(index, stage.Code),
                OperationSequence: stage.Sequence,
                PredecessorOperationIds: predecessorIds,
                DurationMinutes: stage.DurationMinutes,
                RequiredCapabilityCode: IsResourceBlocked(index) && stageIndex == 0
                    ? "CAP-NOT-AVAILABLE"
                    : stage.CapabilityCode,
                EligibleResourceIds: eligibleResources,
                PrimaryResourceId: eligibleResources.ElementAtOrDefault((index - 1) % 6),
                EarliestStartUtc: HorizonStartUtc,
                DueUtc: dueUtc,
                Priority: priority,
                IsRush: isRush,
                SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                MaterialReadyUtc: HorizonStartUtc,
                QualityBlockReason: null,
                SourceReference: $"APS-SCALE:{stage.WorkCenterId}:{stage.Code}",
                SetupMinutes: 5,
                ToolingAvailable: !(IsToolingBlocked(index) && stageIndex == 1));
        }).ToArray();

        return new SchedulingOrderContract(
            OrderId: orderId,
            SkuCode: index % 2 == 0 ? "FG-FRONT-SHOCK" : "FG-REAR-SHOCK",
            Quantity: 20 + (index % 5) * 10,
            DueUtc: dueUtc,
            Priority: priority,
            IsRush: isRush,
            Operations: operations);
    }

    private static SchedulingResourceContract[] CreateResources()
    {
        return Stages.SelectMany(stage => Enumerable.Range(1, 6).Select(resourceIndex =>
            new SchedulingResourceContract(
                ResourceId: ResourceId(stage.ResourcePrefix, resourceIndex),
                WorkCenterId: stage.WorkCenterId,
                CapabilityCodes: [stage.CapabilityCode],
                CapacityUnits: 1,
                CalendarId: "CAL-APS-SCALE",
                SortKey: $"{stage.Sequence:D2}-{resourceIndex:D2}")))
            .ToArray();
    }

    private static SchedulingCalendarContract CreateCalendar()
    {
        var shifts = Enumerable.Range(0, 45)
            .Select(day => new SchedulingTimeWindowContract(
                BenchmarkDateUtc.AddDays(day).AddHours(8),
                BenchmarkDateUtc.AddDays(day).AddHours(20),
                "day-shift"))
            .ToArray();
        return new SchedulingCalendarContract("CAL-APS-SCALE", shifts);
    }

    private static SchedulingUnavailabilityWindowContract[] CreateUnavailabilityWindows()
    {
        return Stages.Select((stage, index) => new SchedulingUnavailabilityWindowContract(
            ResourceId: ResourceId(stage.ResourcePrefix, 1),
            WorkCenterId: stage.WorkCenterId,
            StartUtc: BenchmarkDateUtc.AddDays(10 + index).AddHours(8),
            EndUtc: BenchmarkDateUtc.AddDays(10 + index).AddHours(12),
            ReasonCode: "planned-maintenance"))
            .ToArray();
    }

    private static bool IsMaterialBlocked(int index) => index % 37 == 0;
    private static bool IsQualityBlocked(int index) => index % 41 == 0;
    private static bool IsToolingBlocked(int index) => index % 43 == 0;
    private static bool IsResourceBlocked(int index) => index % 47 == 0;
    private static string OrderId(int index) => $"WO-APS-{index:D4}";
    private static string OperationId(int index, string stage) => $"{OrderId(index)}-{stage}";
    private static string ResourceId(string prefix, int index) => $"DEV-{prefix}-{index:D2}";

    private sealed record OperationStage(
        string Code,
        int Sequence,
        int DurationMinutes,
        string CapabilityCode,
        string WorkCenterId,
        string ResourcePrefix);
}
