using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Inventory;

public static class InventoryIntegrationEventTypes
{
    public const string InventoryMovementRequested = "inventory.InventoryMovementRequested";
    public const string InventoryReservationReleaseRequested = "inventory.InventoryReservationReleaseRequested";
    public const string StockMovementPosted = "inventory.StockMovementPosted";
    public const string StockMovementPostingFailed = "inventory.StockMovementPostingFailed";
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
    public const string BusinessErp = "business-erp";
    public const string BusinessMes = "business-mes";
    public const string BusinessQuality = "business-quality";
}

public static class InventoryMovementSourceServices
{
    public const string Quality = "quality";
}

public static class InventoryMovementRequestTypes
{
    public const string StatusTransfer = "status-transfer";
}

public static class InventoryMovementTypes
{
    public const string Adjustment = "adjustment";
    public const string StatusTransferOut = "status-transfer-out";
    public const string StatusTransferIn = "status-transfer-in";
}

public static class InventoryQualityStatuses
{
    public const string Quality = "quality";
    public const string Unrestricted = "unrestricted";
    public const string Blocked = "blocked";
    public const string Restricted = "restricted";
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
    DateTimeOffset RequestedAtUtc,
    string? InventoryReservationId = null,
    decimal? UnitCost = null,
    string? TargetQualityStatus = null,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null,
    int? ShelfLifeDays = null);

public sealed record InventoryReservationReleaseRequestedIntegrationEvent(
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
    InventoryReservationReleaseRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InventoryReservationReleaseRequestedPayload(
    string ReservationSourceService,
    string SourceDocumentId,
    IReadOnlyCollection<string> SourceDocumentLineIds,
    string Reason,
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
    decimal? MovementAmount,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);

public sealed record StockMovementPostingFailedIntegrationEvent(
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
    StockMovementPostingFailedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record StockMovementPostingFailedPayload(
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
    string FailureCode,
    string FailureMessage,
    DateTimeOffset FailedAtUtc,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);

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
    decimal InventoryValue,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);
