using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.IndustrialTelemetry;

public static class IndustrialTelemetryIntegrationEventTypes
{
    public const string DeviceStateChanged = "industrialTelemetry.DeviceStateChanged";
    public const string AlarmRaised = "industrialTelemetry.AlarmRaised";
    public const string AlarmCleared = "industrialTelemetry.AlarmCleared";
    public const string AlarmEscalated = "industrialTelemetry.AlarmEscalated";
    public const string TelemetryTagCreated = "industrialTelemetry.TelemetryTagCreated";
    public const string TelemetrySampleRecorded = "industrialTelemetry.TelemetrySampleRecorded";
}

public static class IndustrialTelemetryIntegrationEventSources
{
    public const string IndustrialTelemetry = "industrialTelemetry";
}

public static class IndustrialTelemetryIntegrationEventVersions
{
    public const int V1 = 1;
}

public sealed record DeviceStateChangedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    DeviceStateChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record DeviceStateChangedPayload(
    string DeviceStateSnapshotId,
    string DeviceAssetId,
    string CurrentState,
    string SourceSequence);

public sealed record AlarmRaisedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AlarmRaisedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AlarmRaisedPayload(
    string AlarmEventId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId,
    string? Priority = null,
    string? TagKey = null,
    decimal? ObservedValue = null,
    decimal? ThresholdValue = null,
    string? UnitCode = null);

public sealed record AlarmClearedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AlarmClearedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AlarmClearedPayload(
    string AlarmEventId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset ClearedAtUtc,
    string ExternalAlarmId);

public sealed record AlarmEscalatedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    AlarmEscalatedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AlarmEscalatedPayload(
    string AlarmEventId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset EscalatedAtUtc,
    string ExternalAlarmId,
    string EscalationReason,
    IReadOnlyCollection<string> RecipientRefs,
    string? Priority = null);
