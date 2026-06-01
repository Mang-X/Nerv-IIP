using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.EquipmentRuntime;

public static class EquipmentRuntimeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new EquipmentRuntimeSourceTypeJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public static class EquipmentRuntimeReasonCodes
{
    public const string ActiveAlarm = "equipment.activeAlarm";
    public const string StateUnavailable = "equipment.stateUnavailable";
    public const string Downtime = "equipment.downtime";
    public const string MaintenanceWindow = "equipment.maintenanceWindow";
    public const string InspectionRequired = "equipment.inspectionRequired";
    public const string SourceStale = "equipment.sourceStale";
    public const string TagMappingMissing = "equipment.tagMappingMissing";
    public const string NoEligibleSubstitute = "equipment.noEligibleSubstitute";
}

public sealed record EquipmentRuntimeAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<string>? DeviceAssetIds,
    IReadOnlyCollection<string>? WorkCenterIds,
    int FreshnessMaxAgeMinutes = 60);

public sealed record EquipmentRuntimeAvailabilityResponse(
    int ContractVersion,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset QueryWindowStartUtc,
    DateTimeOffset QueryWindowEndUtc,
    IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract> Items);

public sealed record EquipmentRuntimeAvailabilityWindowContract(
    string DeviceAssetId,
    string? WorkCenterId,
    EquipmentRuntimeAvailabilityStatus AvailabilityStatus,
    string ReasonCode,
    EquipmentRuntimeSeverity Severity,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    EquipmentRuntimeSourceType SourceType,
    string SourceReferenceId,
    string MessageKey,
    IReadOnlyCollection<string> SubstituteDeviceAssetIds);

public sealed record EquipmentRuntimeCurrentStateResponse(
    int ContractVersion,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? CurrentState,
    DateTimeOffset? StateOccurredAtUtc,
    bool IsSourceFresh,
    IReadOnlyCollection<EquipmentRuntimeAlarmSummary> ActiveAlarms);

public sealed record EquipmentRuntimeAlarmSummary(
    string AlarmEventId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId);

public enum EquipmentRuntimeAvailabilityStatus
{
    Available = 0,
    Unavailable = 1,
    Unknown = 2
}

public enum EquipmentRuntimeSeverity
{
    Info = 0,
    Warning = 1,
    Blocked = 2,
    Critical = 3
}

[JsonConverter(typeof(EquipmentRuntimeSourceTypeJsonConverter))]
public enum EquipmentRuntimeSourceType
{
    DeviceState = 0,
    Alarm = 1,
    Downtime = 2,
    MaintenanceWindow = 3,
    Inspection = 4,
    StaleSource = 5,
    ManualBlock = 6
}

public sealed class EquipmentRuntimeSourceTypeJsonConverter : JsonConverter<EquipmentRuntimeSourceType>
{
    private static readonly IReadOnlyDictionary<EquipmentRuntimeSourceType, string> Names = new Dictionary<EquipmentRuntimeSourceType, string>
    {
        [EquipmentRuntimeSourceType.DeviceState] = "device-state",
        [EquipmentRuntimeSourceType.Alarm] = "alarm",
        [EquipmentRuntimeSourceType.Downtime] = "downtime",
        [EquipmentRuntimeSourceType.MaintenanceWindow] = "maintenance-window",
        [EquipmentRuntimeSourceType.Inspection] = "inspection",
        [EquipmentRuntimeSourceType.StaleSource] = "stale-source",
        [EquipmentRuntimeSourceType.ManualBlock] = "manual-block",
    };

    private static readonly IReadOnlyDictionary<string, EquipmentRuntimeSourceType> Values =
        Names.SelectMany(x => new[]
            {
                new KeyValuePair<string, EquipmentRuntimeSourceType>(x.Value, x.Key),
                new KeyValuePair<string, EquipmentRuntimeSourceType>(x.Key.ToString(), x.Key),
                new KeyValuePair<string, EquipmentRuntimeSourceType>(JsonNamingPolicy.CamelCase.ConvertName(x.Key.ToString()), x.Key),
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);

    public override EquipmentRuntimeSourceType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is not null && Values.TryGetValue(value, out var sourceType))
        {
            return sourceType;
        }

        throw new JsonException($"Unknown equipment runtime source type: {value}");
    }

    public override void Write(Utf8JsonWriter writer, EquipmentRuntimeSourceType value, JsonSerializerOptions options)
    {
        if (!Names.TryGetValue(value, out var name))
        {
            throw new JsonException($"Unknown equipment runtime source type: {value}");
        }

        writer.WriteStringValue(name);
    }
}
