using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public class EquipmentAvailabilitySchedulingAdapterTests
{
    private static readonly DateTimeOffset QueryStartUtc = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset QueryEndUtc = new(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 6, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToUnavailabilityWindows_maps_unavailable_equipment_windows()
    {
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-OIL-01",
                workCenterId: "WC-OIL-SEAL",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.Downtime,
                startUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                endUtc: new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero)));

        var windows = EquipmentAvailabilitySchedulingAdapter.ToUnavailabilityWindows(availability);

        var window = Assert.Single(windows);
        Assert.Equal("DEV-OIL-01", window.ResourceId);
        Assert.Equal("WC-OIL-SEAL", window.WorkCenterId);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero), window.EndUtc);
        Assert.Equal(EquipmentRuntimeReasonCodes.Downtime, window.ReasonCode);
    }

    [Fact]
    public void ToUnavailabilityWindows_keeps_only_real_unavailability_and_turns_unknown_into_data_risk()
    {
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Available,
                reasonCode: "normal",
                startUtc: QueryStartUtc,
                endUtc: QueryEndUtc),
            CreateWindow(
                deviceAssetId: "DEV-ROD-01",
                workCenterId: "WC-ROD-ASSEMBLY",
                status: EquipmentRuntimeAvailabilityStatus.Unknown,
                reasonCode: EquipmentRuntimeReasonCodes.SourceStale,
                startUtc: QueryStartUtc,
                endUtc: QueryStartUtc.AddHours(1)));

        var windows = EquipmentAvailabilitySchedulingAdapter.ToUnavailabilityWindows(availability);
        var risks = EquipmentAvailabilitySchedulingAdapter.ToEquipmentDataRisks(availability);

        // Available 不产生任何窗口;Unknown(采集过期)是数据盲区,不是「设备不可用」——
        // 它只登记设备数据风险,不能变成硬不可用窗口(#1320)。
        Assert.Empty(windows);
        var risk = Assert.Single(risks);
        Assert.Equal("DEV-ROD-01", risk.ResourceId);
        Assert.Equal(EquipmentRuntimeReasonCodes.SourceStale, risk.ReasonCode);
    }

    [Fact]
    public void ToUnavailabilityWindows_keeps_unknown_hard_when_mode_is_hard()
    {
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-ROD-01",
                workCenterId: "WC-ROD-ASSEMBLY",
                status: EquipmentRuntimeAvailabilityStatus.Unknown,
                reasonCode: EquipmentRuntimeReasonCodes.SourceStale,
                startUtc: QueryStartUtc,
                endUtc: QueryStartUtc.AddHours(1)));

        var windows = EquipmentAvailabilitySchedulingAdapter.ToUnavailabilityWindows(
            availability,
            SchedulingEquipmentUnknownModeContract.Hard);
        var risks = EquipmentAvailabilitySchedulingAdapter.ToEquipmentDataRisks(
            availability,
            SchedulingEquipmentUnknownModeContract.Hard);

        var window = Assert.Single(windows);
        Assert.Equal(EquipmentRuntimeReasonCodes.SourceStale, window.ReasonCode);
        // 硬口径下已经进了不可用窗口,不再重复登记风险。
        Assert.Empty(risks);
    }

    [Fact]
    public void ToUnavailabilityWindows_filters_zero_length_and_reversed_runtime_windows()
    {
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                startUtc: QueryStartUtc,
                endUtc: QueryStartUtc),
            CreateWindow(
                deviceAssetId: "DEV-ROD-01",
                workCenterId: "WC-ROD-ASSEMBLY",
                status: EquipmentRuntimeAvailabilityStatus.Unknown,
                reasonCode: EquipmentRuntimeReasonCodes.SourceStale,
                startUtc: QueryStartUtc.AddHours(2),
                endUtc: QueryStartUtc.AddHours(1)),
            CreateWindow(
                deviceAssetId: "DEV-OIL-01",
                workCenterId: "WC-OIL-SEAL",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.Downtime,
                startUtc: QueryStartUtc.AddHours(3),
                endUtc: QueryStartUtc.AddHours(4)));

        var windows = EquipmentAvailabilitySchedulingAdapter.ToUnavailabilityWindows(availability);
        var risks = EquipmentAvailabilitySchedulingAdapter.ToEquipmentDataRisks(availability);

        var window = Assert.Single(windows);
        Assert.Equal("DEV-OIL-01", window.ResourceId);
        Assert.Equal(EquipmentRuntimeReasonCodes.Downtime, window.ReasonCode);
        // 起止颠倒的窗口既不是硬阻也不是风险,一律丢弃。
        Assert.Empty(risks);
    }

    [Fact]
    public void Apply_rejects_context_mismatch()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(organizationId: "other-org");

        var exception = Assert.Throws<ArgumentException>(() =>
            EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability));

        Assert.Equal("availability", exception.ParamName);
        Assert.Contains("Equipment runtime availability context does not match scheduling problem context.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_appends_runtime_windows_with_stable_sorting()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                startUtc: QueryStartUtc.AddHours(1),
                endUtc: QueryStartUtc.AddHours(2)));

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability);

        Assert.Equal(problem.UnavailabilityWindows.Count + 1, applied.UnavailabilityWindows.Count);
        Assert.Equal(
            applied.UnavailabilityWindows
                .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
                .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
                .ThenBy(x => x.StartUtc)
                .ThenBy(x => x.EndUtc)
                .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
                .ToArray(),
            applied.UnavailabilityWindows);
        Assert.Contains(applied.UnavailabilityWindows, x =>
            x.ResourceId == "DEV-WELD-01"
            && x.WorkCenterId == "WC-TUBE-WELD"
            && x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
    }

    [Fact]
    public void Apply_filters_invalid_runtime_windows_without_changing_existing_windows()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var existingWindow = Assert.Single(problem.UnavailabilityWindows);
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                startUtc: QueryStartUtc,
                endUtc: QueryStartUtc),
            CreateWindow(
                deviceAssetId: "DEV-ROD-01",
                workCenterId: "WC-ROD-ASSEMBLY",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.StateUnavailable,
                startUtc: QueryStartUtc.AddHours(2),
                endUtc: QueryStartUtc.AddHours(1)));

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability);

        var window = Assert.Single(applied.UnavailabilityWindows);
        Assert.Equal(existingWindow, window);
    }

    [Fact]
    public void Apply_runtime_block_to_shock_absorber_fixture_causes_scheduler_equipment_conflict()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                startUtc: problem.HorizonStartUtc,
                endUtc: problem.HorizonEndUtc));
        var scheduler = new FiniteCapacityScheduler();

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability);
        var plan = scheduler.Schedule(applied, "plan-runtime-equipment-block-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-RUSH-REAR-001-WELD"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
    }

    /// <summary>
    /// #1320 走查形状:采集源不可达时上游对**每台设备**发一条覆盖全排程窗口的 Unknown 窗口。
    /// 软口径(默认)下这不能阻断任何工序——否则方案 0 已排、发布守卫必拒,锁定→发布链结构性不可达。
    /// </summary>
    [Fact]
    public void Apply_soft_mode_keeps_every_operation_schedulable_when_all_devices_have_no_snapshot()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(problem.Resources
            .Select(resource => CreateWindow(
                deviceAssetId: resource.ResourceId,
                workCenterId: resource.WorkCenterId,
                status: EquipmentRuntimeAvailabilityStatus.Unknown,
                reasonCode: HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode,
                startUtc: problem.HorizonStartUtc,
                endUtc: problem.HorizonEndUtc))
            .ToArray());
        var operationCount = problem.Orders.Sum(x => x.Operations.Count);

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability);
        var plan = new FiniteCapacityScheduler().Schedule(applied, "plan-1320-soft", GeneratedAtUtc);

        Assert.Empty(plan.UnscheduledOperations);
        Assert.Equal(operationCount, plan.Assignments.Count);
        // 「不知道」以设备数据风险的形式随计划带出,而不是悄悄放行。
        Assert.Equal(operationCount, (plan.EquipmentRisks ?? []).Count);
        Assert.Equal(operationCount, plan.Metrics.EquipmentRiskOperationCount);
        Assert.All(plan.GanttItems, x => Assert.True(x.HasEquipmentRisk));
        // 风险只到预警级,发布守卫(只拒 error 级冲突 + 未排工序)因此放行。
        Assert.All(
            plan.Conflicts.Where(x => x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment),
            x => Assert.Equal(ScheduleConflictSeverityContract.Warning, x.Severity));
        Assert.DoesNotContain(plan.Conflicts, x => x.Severity == ScheduleConflictSeverityContract.Error);
    }

    /// <summary>
    /// 对照组:硬口径保留旧行为——正是 #1320 的故障形状(全设备无快照 → 全部工序不可排)。
    /// </summary>
    [Fact]
    public void Apply_hard_mode_still_blocks_every_operation_when_all_devices_have_no_snapshot()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(problem.Resources
            .Select(resource => CreateWindow(
                deviceAssetId: resource.ResourceId,
                workCenterId: resource.WorkCenterId,
                status: EquipmentRuntimeAvailabilityStatus.Unknown,
                reasonCode: HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode,
                startUtc: problem.HorizonStartUtc,
                endUtc: problem.HorizonEndUtc))
            .ToArray());

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(
            problem,
            availability,
            SchedulingEquipmentUnknownModeContract.Hard);
        var plan = new FiniteCapacityScheduler().Schedule(applied, "plan-1320-hard", GeneratedAtUtc);

        Assert.Empty(plan.Assignments);
        Assert.NotEmpty(plan.UnscheduledOperations);
        Assert.Empty(plan.EquipmentRisks ?? []);
    }

    /// <summary>
    /// 软化只针对「状态未知」。真实停机窗口仍然是硬阻——这条回归防止把设备约束整个放空。
    /// </summary>
    [Fact]
    public void Apply_soft_mode_still_hard_blocks_real_downtime()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Unavailable,
                reasonCode: EquipmentRuntimeReasonCodes.Downtime,
                startUtc: problem.HorizonStartUtc,
                endUtc: problem.HorizonEndUtc));

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability);
        var plan = new FiniteCapacityScheduler().Schedule(applied, "plan-1320-downtime", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-RUSH-REAR-001-WELD"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
        Assert.Empty(plan.EquipmentRisks ?? []);
    }

    /// <summary>
    /// 设备数据风险按「排到哪台设备的哪个时段」登记:风险窗口不覆盖的工序不背这个标记。
    /// </summary>
    [Fact]
    public void Apply_soft_mode_marks_only_operations_placed_on_the_unknown_device()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var availability = CreateAvailability(
            CreateWindow(
                deviceAssetId: "DEV-WELD-01",
                workCenterId: "WC-TUBE-WELD",
                status: EquipmentRuntimeAvailabilityStatus.Unknown,
                reasonCode: EquipmentRuntimeReasonCodes.SourceStale,
                startUtc: problem.HorizonStartUtc,
                endUtc: problem.HorizonEndUtc));

        var applied = EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability);
        var plan = new FiniteCapacityScheduler().Schedule(applied, "plan-1320-scoped", GeneratedAtUtc);

        Assert.Empty(plan.UnscheduledOperations);
        Assert.NotEmpty(plan.EquipmentRisks ?? []);
        Assert.All(plan.EquipmentRisks!, x => Assert.Equal("DEV-WELD-01", x.ResourceId));
        Assert.All(
            plan.GanttItems.Where(x => x.ResourceId != "DEV-WELD-01"),
            x => Assert.False(x.HasEquipmentRisk));
        Assert.Contains(plan.EquipmentRisks!, x => x.Message.Contains("采集数据已过期", StringComparison.Ordinal));
    }

    private static EquipmentRuntimeAvailabilityResponse CreateAvailability(
        params EquipmentRuntimeAvailabilityWindowContract[] items)
    {
        return CreateAvailability("org-001", "prod", items);
    }

    private static EquipmentRuntimeAvailabilityResponse CreateAvailability(
        string organizationId = "org-001",
        string environmentId = "prod",
        params EquipmentRuntimeAvailabilityWindowContract[] items)
    {
        return new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: organizationId,
            EnvironmentId: environmentId,
            QueryWindowStartUtc: QueryStartUtc,
            QueryWindowEndUtc: QueryEndUtc,
            Items: items);
    }

    private static EquipmentRuntimeAvailabilityWindowContract CreateWindow(
        string deviceAssetId,
        string? workCenterId,
        EquipmentRuntimeAvailabilityStatus status,
        string reasonCode,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        return new EquipmentRuntimeAvailabilityWindowContract(
            DeviceAssetId: deviceAssetId,
            WorkCenterId: workCenterId,
            AvailabilityStatus: status,
            ReasonCode: reasonCode,
            Severity: EquipmentRuntimeSeverity.Blocked,
            StartUtc: startUtc,
            EndUtc: endUtc,
            SourceType: EquipmentRuntimeSourceType.Alarm,
            SourceReferenceId: $"runtime:{deviceAssetId}",
            MessageKey: "equipment-runtime.availability",
            SubstituteDeviceAssetIds: []);
    }
}
