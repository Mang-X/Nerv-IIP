namespace Nerv.IIP.Contracts.IndustrialTelemetry;

public static class IndustrialTelemetryIntegrationEventTypes
{
    public const string DeviceStateChanged = "industrialTelemetry.DeviceStateChanged";
    public const string AlarmRaised = "industrialTelemetry.AlarmRaised";
    public const string AlarmCleared = "industrialTelemetry.AlarmCleared";
    public const string TelemetryTagCreated = "industrialTelemetry.TelemetryTagCreated";
    public const string TelemetrySampleRecorded = "industrialTelemetry.TelemetrySampleRecorded";
}

public static class IndustrialTelemetryIntegrationEventSources
{
    public const string IndustrialTelemetry = "industrialTelemetry";
}

public sealed record DeviceStateChangedIntegrationEvent(
    string EventId,
    string EventType,
    string DeviceStateSnapshotId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string CurrentState,
    DateTimeOffset OccurredAtUtc,
    string SourceSequence);

public sealed record AlarmRaisedIntegrationEvent(
    string EventId,
    string EventType,
    string AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId);

public sealed record AlarmClearedIntegrationEvent(
    string EventId,
    string EventType,
    string AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset ClearedAtUtc,
    string ExternalAlarmId);
