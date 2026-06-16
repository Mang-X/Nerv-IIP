using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Inventory;

public static class InventoryIntegrationEventTypes
{
    public const string InventoryMovementRequested = "inventory.InventoryMovementRequested";
    public const string StockMovementPosted = "inventory.StockMovementPosted";
    public const string StockCountVarianceConfirmed = "inventory.StockCountVarianceConfirmed";
    public const string StockAvailabilityChanged = "inventory.StockAvailabilityChanged";
}

public static class InventoryIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class InventoryIntegrationEventSources
{
    public const string BusinessInventory = "business-inventory";
    public const string BusinessWms = "business-wms";
}

public sealed record InventoryMovementRequestedIntegrationEvent(
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
    InventoryMovementRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InventoryMovementRequestedPayload(
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    DateTimeOffset RequestedAtUtc);

public sealed record StockMovementPostedIntegrationEvent(
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
    StockMovementPostedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record StockMovementPostedPayload(
    string InventoryMovementId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    DateTimeOffset PostedAtUtc,
    decimal? UnitCost,
    decimal? MovementAmount);

public sealed record StockCountVarianceConfirmedIntegrationEvent(
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
    StockCountVarianceConfirmedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record StockCountVarianceConfirmedPayload(
    string CountTaskCode,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal? CountedQuantity,
    decimal VarianceQuantity,
    DateTimeOffset ConfirmedAtUtc);

public sealed record StockAvailabilityChangedIntegrationEvent(
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
    StockAvailabilityChangedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record StockAvailabilityChangedPayload(
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    long LedgerVersion,
    DateTimeOffset ChangedAtUtc,
    decimal MovingAverageUnitCost,
    decimal InventoryValue);
