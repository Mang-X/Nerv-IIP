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
    public void ToUnavailabilityWindows_filters_available_windows()
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

        var window = Assert.Single(windows);
        Assert.Equal("DEV-ROD-01", window.ResourceId);
        Assert.Equal(EquipmentRuntimeReasonCodes.SourceStale, window.ReasonCode);
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

        var window = Assert.Single(windows);
        Assert.Equal("DEV-OIL-01", window.ResourceId);
        Assert.Equal(EquipmentRuntimeReasonCodes.Downtime, window.ReasonCode);
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
