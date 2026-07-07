using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.AppHubQueries;

public static class AppHubIntegrationEventTypes
{
    public const string ConnectorHostUnreachable = "apphub.ConnectorHostUnreachable";
    public const string ConnectorHostRestored = "apphub.ConnectorHostRestored";
}

public static class AppHubIntegrationEventSources
{
    public const string AppHub = "apphub";
}

public static class AppHubIntegrationEventVersions
{
    public const int V1 = 1;
}

public sealed record ConnectorHostUnreachableIntegrationEvent(
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
    ConnectorHostUnreachablePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ConnectorHostUnreachablePayload(
    string ConnectorHostId,
    string InstanceKey,
    DateTimeOffset LastHeartbeatAtUtc,
    DateTimeOffset DetectedAtUtc,
    int HeartbeatTimeoutSeconds);

public sealed record ConnectorHostRestoredIntegrationEvent(
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
    ConnectorHostRestoredPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ConnectorHostRestoredPayload(
    string ConnectorHostId,
    string InstanceKey,
    DateTimeOffset RestoredAtUtc);
