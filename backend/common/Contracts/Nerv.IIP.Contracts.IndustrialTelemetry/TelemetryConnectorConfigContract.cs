namespace Nerv.IIP.Contracts.IndustrialTelemetry;

public sealed record TelemetryConnectorMapping(
    string DeviceAssetId,
    string TagKey,
    string UnitCode,
    int BucketSeconds = 5,
    byte UnitId = 1,
    ushort Address = 40001,
    string DataType = "Float32",
    string? TopicFilter = null,
    string? ValueJsonPath = null,
    string? NodeId = null);

public sealed record TelemetryConnectorConfigInput(
    string ConnectorId,
    string DisplayName,
    string Protocol,
    string Endpoint,
    bool IsEnabled,
    int PollIntervalSeconds,
    IReadOnlyList<TelemetryConnectorMapping> Mappings);

public sealed record TelemetryConnectorConfigItem(
    string ConnectorId,
    string DisplayName,
    string Protocol,
    string Endpoint,
    bool IsEnabled,
    int PollIntervalSeconds,
    IReadOnlyList<TelemetryConnectorMapping> Mappings,
    Guid Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record TelemetryConnectionTestInput(string Protocol, string Endpoint, TelemetryConnectorMapping? Mapping);
public sealed record TelemetryConnectionTestResult(bool Reachable, long LatencyMs, string Message, decimal? SamplePreview);
