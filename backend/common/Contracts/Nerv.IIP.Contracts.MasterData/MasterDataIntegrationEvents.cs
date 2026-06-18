using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.MasterData;

public static class MasterDataIntegrationEventTypes
{
    public const string SkuChanged = "masterData.SkuChanged";
    public const string SkuDisabled = "masterData.SkuDisabled";
    public const string UnitOfMeasureChanged = "masterData.UnitOfMeasureChanged";
    public const string BusinessPartnerChanged = "masterData.BusinessPartnerChanged";
    public const string ResourceChanged = "masterData.ResourceChanged";
    public const string WorkCalendarChanged = "masterData.WorkCalendarChanged";
    public const string DeviceAssetChanged = "masterData.DeviceAssetChanged";
    public const string ReferenceDataCodeChanged = "masterData.ReferenceDataCodeChanged";
}

public static class MasterDataIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class MasterDataIntegrationEventSources
{
    public const string BusinessMasterData = "business-masterdata";
}

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
    MasterDataChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    MasterDataDisabledPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    MasterDataChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    MasterDataChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    ResourceChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    MasterDataChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    MasterDataChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
    ReferenceDataChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

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
