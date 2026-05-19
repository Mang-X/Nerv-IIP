namespace Nerv.IIP.AppHub.Web.Application.IntegrationEvents;

public sealed record ApplicationInstanceStatusChangedIntegrationEvent(
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
    ApplicationInstanceStatusChangedPayload Payload);

public sealed record ApplicationInstanceStatusChangedPayload(
    string InstanceKey,
    string PreviousStatus,
    string CurrentStatus,
    DateTimeOffset ChangedAtUtc);
