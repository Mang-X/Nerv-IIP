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

public enum EquipmentRuntimeDeviceStateCategory
{
    Productive = 0,
    LoadingNonProductive = 1,
    PlannedDown = 2,
    Unavailable = 3,
    Unknown = 4
}

public static class EquipmentRuntimeDeviceStates
{
    public const string Running = "running";
    public const string Available = "available";
    public const string Idle = "idle";
    public const string Ready = "ready";
    public const string Standby = "standby";
    public const string PlannedDown = "planned-down";
    public const string Stopped = "stopped";
    public const string Faulted = "faulted";

    private static readonly IReadOnlyDictionary<string, EquipmentRuntimeDeviceStateCategory> Categories =
        new Dictionary<string, EquipmentRuntimeDeviceStateCategory>(StringComparer.OrdinalIgnoreCase)
        {
            [Running] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["run"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["operating"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["active"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["producing"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["machining"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["in-production"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["运行"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["运行中"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["加工"] = EquipmentRuntimeDeviceStateCategory.Productive,
            ["生产中"] = EquipmentRuntimeDeviceStateCategory.Productive,

            [Available] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            [Idle] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            [Ready] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            [Standby] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            ["waiting"] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            ["就绪"] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            ["空闲"] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,
            ["待机"] = EquipmentRuntimeDeviceStateCategory.LoadingNonProductive,

            [PlannedDown] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["planned-stop"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["planned-maintenance"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["maintenance-window"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["scheduled-maintenance"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["planned-outage"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["pm"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["计划停机"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["计划维护"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,
            ["预防维护"] = EquipmentRuntimeDeviceStateCategory.PlannedDown,

            [Stopped] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["stop"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["down"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            [Faulted] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["fault"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["error"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["unavailable"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["breakdown"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["unplanned-down"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["emergency-stop"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["offline"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["停止"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["停机"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["故障"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
            ["离线"] = EquipmentRuntimeDeviceStateCategory.Unavailable,
        };

    public static EquipmentRuntimeDeviceStateCategory Classify(string? state)
    {
        var normalized = Normalize(state);
        return normalized is not null && Categories.TryGetValue(normalized, out var category)
            ? category
            : EquipmentRuntimeDeviceStateCategory.Unknown;
    }

    public static bool IsProductiveRuntime(string? state)
    {
        return Classify(state) == EquipmentRuntimeDeviceStateCategory.Productive;
    }

    public static bool IsPlannedDownState(string? state)
    {
        return Classify(state) == EquipmentRuntimeDeviceStateCategory.PlannedDown;
    }

    public static bool IsRuntimeAvailable(string? state)
    {
        return Classify(state) is EquipmentRuntimeDeviceStateCategory.Productive or EquipmentRuntimeDeviceStateCategory.LoadingNonProductive;
    }

    private static string? Normalize(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var normalized = state.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized;
    }
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
