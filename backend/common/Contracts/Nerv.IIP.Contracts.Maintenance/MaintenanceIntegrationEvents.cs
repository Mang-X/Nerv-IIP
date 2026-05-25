using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Maintenance;

public static class MaintenanceIntegrationEventTypes
{
    public const string AssetUnavailable = "maintenance.AssetUnavailable";
    public const string AssetRestored = "maintenance.AssetRestored";
}

public static class MaintenanceIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class MaintenanceIntegrationEventSources
{
    public const string Maintenance = "maintenance";
}

public sealed record AssetUnavailableIntegrationEvent(
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
    AssetUnavailablePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AssetUnavailablePayload(
    string DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc);

public sealed record AssetRestoredIntegrationEvent(
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
    AssetRestoredPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record AssetRestoredPayload(
    string DeviceAssetId,
    DateTimeOffset RestoredAtUtc);
