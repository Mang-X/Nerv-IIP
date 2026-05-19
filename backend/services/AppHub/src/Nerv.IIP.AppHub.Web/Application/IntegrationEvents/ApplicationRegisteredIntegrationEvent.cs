namespace Nerv.IIP.AppHub.Web.Application.IntegrationEvents;

public sealed record ApplicationRegisteredIntegrationEvent(
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
    ApplicationRegisteredPayload Payload);

public sealed record ApplicationRegisteredPayload(
    string ApplicationKey,
    string Version);
