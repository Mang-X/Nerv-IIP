namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEvents;

public static class InventoryIntegrationEventTypes
{
    public const string StockMovementPosted = "inventory.StockMovementPosted";
    public const string StockCountVarianceConfirmed = "inventory.StockCountVarianceConfirmed";
    public const string StockAvailabilityChanged = "inventory.StockAvailabilityChanged";
}

public static class InventoryIntegrationEventSources
{
    public const string BusinessInventory = "business-inventory";
}

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
    StockMovementPostedPayload Payload);

public sealed record StockMovementPostedPayload(
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
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
    DateTimeOffset PostedAtUtc);

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
    StockCountVarianceConfirmedPayload Payload);

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
    StockAvailabilityChangedPayload Payload);

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
    DateTimeOffset ChangedAtUtc);
