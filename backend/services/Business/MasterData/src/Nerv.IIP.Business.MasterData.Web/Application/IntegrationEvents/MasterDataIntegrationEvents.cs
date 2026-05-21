namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEvents;

public sealed record SkuChangedIntegrationEvent(
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
    MasterDataChangedPayload Payload);

public sealed record SkuDisabledIntegrationEvent(
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
    MasterDataDisabledPayload Payload);

public sealed record UnitOfMeasureChangedIntegrationEvent(
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
    MasterDataChangedPayload Payload);

public sealed record BusinessPartnerChangedIntegrationEvent(
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
    MasterDataChangedPayload Payload);

public sealed record ResourceChangedIntegrationEvent(
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
    ResourceChangedPayload Payload);

public sealed record WorkCalendarChangedIntegrationEvent(
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
    MasterDataChangedPayload Payload);

public sealed record DeviceAssetChangedIntegrationEvent(
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
    MasterDataChangedPayload Payload);

public sealed record ReferenceDataCodeChangedIntegrationEvent(
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
    ReferenceDataChangedPayload Payload);

public sealed record MasterDataChangedPayload(
    string ResourceType,
    string Code,
    string Status,
    DateTimeOffset ChangedAtUtc);

public sealed record MasterDataDisabledPayload(
    string ResourceType,
    string Code,
    string Status,
    string DisabledReason,
    DateTimeOffset ChangedAtUtc);

public sealed record ResourceChangedPayload(
    string ResourceType,
    string Code,
    string Status,
    DateTimeOffset ChangedAtUtc);

public sealed record ReferenceDataChangedPayload(
    string CodeSet,
    string Code,
    string Status,
    DateTimeOffset ChangedAtUtc);
