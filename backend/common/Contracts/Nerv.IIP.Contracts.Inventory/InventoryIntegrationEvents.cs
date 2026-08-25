using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Inventory;

public static class InventoryIntegrationEventTypes
{
    public const string InventoryMovementRequested = "inventory.InventoryMovementRequested";
    public const string InventoryReservationReleaseRequested = "inventory.InventoryReservationReleaseRequested";
    public const string StockReservationExpired = "inventory.StockReservationExpired";
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

    /// <summary>WMS 侧发起的库存移动/预留所写的来源服务短名（与事件信封来源 <c>business-wms</c> 是两个面）。</summary>
    public const string Wms = "wms";

    /// <summary>
    /// Maintenance 侧发起的备件出库所写的来源服务短名（载荷 <c>SourceService</c> 面）。
    /// 与事件信封来源 <c>Nerv.IIP.Contracts.Maintenance.MaintenanceIntegrationEventSources.Maintenance</c>
    /// 恰好同值，但是**两个面**：前者是「这笔库存流水由谁发起」，后者是「这条集成事件由谁发布」，
    /// 各自独立演化，不可互相引用（#1370 ③ 批次 D 补值；消费端 Inventory 对该字段只透传、无白名单校验）。
    /// </summary>
    public const string Maintenance = "maintenance";
}

public static class InventoryMovementRequestTypes
{
    public const string StatusTransfer = "status-transfer";
}

public static class InventoryMovementUnitCostAuthorityReferences
{
    /// <summary>
    /// Optional V1 marker for MES finished-goods receipts. It selects the internal
    /// authority lookup policy; it is not itself proof of a unit cost.
    /// </summary>
    public const string MesFinishedGoodsReceipt = "mes-finished-goods-receipt-authority-v1";
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
    int? ShelfLifeDays = null,
    string? UnitCostAuthorityReference = null);

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

public sealed record InventoryReservationExpiredIntegrationEvent(
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
    InventoryReservationExpiredPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record InventoryReservationExpiredPayload(
    string ReservationId,
    string ReservationSourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    decimal ReleasedQuantity,
    DateTimeOffset ExpiresAtUtc);

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
