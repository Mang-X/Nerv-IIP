using System.Text.Json;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Contracts.EquipmentRuntime.Tests;

public sealed class EquipmentRuntimeContractSerializationTests
{
    [Fact]
    public void Availability_response_serializes_status_as_camel_case_and_source_type_as_kebab_case_strings()
    {
        var response = CreateAvailabilityResponse();

        var json = JsonSerializer.Serialize(response, EquipmentRuntimeJson.Options);

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal("unavailable", item.GetProperty("availabilityStatus").GetString());
        Assert.Equal("alarm", item.GetProperty("sourceType").GetString());
    }

    [Theory]
    [InlineData(EquipmentRuntimeSourceType.DeviceState, "device-state")]
    [InlineData(EquipmentRuntimeSourceType.Alarm, "alarm")]
    [InlineData(EquipmentRuntimeSourceType.Downtime, "downtime")]
    [InlineData(EquipmentRuntimeSourceType.MaintenanceWindow, "maintenance-window")]
    [InlineData(EquipmentRuntimeSourceType.Inspection, "inspection")]
    [InlineData(EquipmentRuntimeSourceType.StaleSource, "stale-source")]
    [InlineData(EquipmentRuntimeSourceType.ManualBlock, "manual-block")]
    public void Source_type_serializes_as_stable_kebab_case_contract_value(
        EquipmentRuntimeSourceType sourceType,
        string expected)
    {
        var json = JsonSerializer.Serialize(sourceType, EquipmentRuntimeJson.Options);
        var roundTrip = JsonSerializer.Deserialize<EquipmentRuntimeSourceType>(json, EquipmentRuntimeJson.Options);

        Assert.Equal($"\"{expected}\"", json);
        Assert.Equal(sourceType, roundTrip);
    }

    [Fact]
    public void Availability_response_round_trips_reason_code_and_severity()
    {
        var response = CreateAvailabilityResponse();

        var json = JsonSerializer.Serialize(response, EquipmentRuntimeJson.Options);
        var roundTrip = JsonSerializer.Deserialize<EquipmentRuntimeAvailabilityResponse>(json, EquipmentRuntimeJson.Options);

        Assert.NotNull(roundTrip);
        var item = Assert.Single(roundTrip!.Items);
        Assert.Equal(EquipmentRuntimeReasonCodes.ActiveAlarm, item.ReasonCode);
        Assert.Equal(EquipmentRuntimeSeverity.Critical, item.Severity);
    }

    [Fact]
    public void P0_reason_code_catalog_contains_required_codes()
    {
        var codes = new[]
        {
            EquipmentRuntimeReasonCodes.ActiveAlarm,
            EquipmentRuntimeReasonCodes.StateUnavailable,
            EquipmentRuntimeReasonCodes.Downtime,
            EquipmentRuntimeReasonCodes.MaintenanceWindow,
            EquipmentRuntimeReasonCodes.InspectionRequired,
            EquipmentRuntimeReasonCodes.SourceStale,
            EquipmentRuntimeReasonCodes.TagMappingMissing,
            EquipmentRuntimeReasonCodes.NoEligibleSubstitute
        };

        Assert.Equal(
            [
                "equipment.activeAlarm",
                "equipment.stateUnavailable",
                "equipment.downtime",
                "equipment.maintenanceWindow",
                "equipment.inspectionRequired",
                "equipment.sourceStale",
                "equipment.tagMappingMissing",
                "equipment.noEligibleSubstitute"
            ],
            codes);
    }

    private static EquipmentRuntimeAvailabilityResponse CreateAvailabilityResponse()
    {
        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(2);

        return new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            QueryWindowStartUtc: start,
            QueryWindowEndUtc: end,
            Items:
            [
                new EquipmentRuntimeAvailabilityWindowContract(
                    DeviceAssetId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unavailable,
                    ReasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                    Severity: EquipmentRuntimeSeverity.Critical,
                    StartUtc: start,
                    EndUtc: end,
                    SourceType: EquipmentRuntimeSourceType.Alarm,
                    SourceReferenceId: "alarm-001",
                    MessageKey: "equipment.runtime.alarm.active",
                    SubstituteDeviceAssetIds: ["DEV-OIL-02"])
            ]);
    }
}
