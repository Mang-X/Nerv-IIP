using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Erp;

public static class ErpIntegrationEventTypes
{
    public const string PurchaseRequisitionCreated = "erp.PurchaseRequisitionCreated";
    public const string PurchaseOrderReleased = "erp.PurchaseOrderReleased";
    public const string PurchaseReceiptRecorded = "erp.PurchaseReceiptRecorded";
    public const string DeliveryOrderReleased = "erp.DeliveryOrderReleased";
    public const string AccountPayableCreated = "erp.AccountPayableCreated";
    public const string AccountReceivableCreated = "erp.AccountReceivableCreated";
    public const string CostCandidateCreated = "erp.CostCandidateCreated";
    public const string JournalVoucherPosted = "erp.JournalVoucherPosted";
}

public static class ErpIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class ErpIntegrationEventSources
{
    public const string BusinessErp = "business-erp";
}

public sealed record ErpIntegrationEvent<TPayload>(
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
    TPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record PurchaseRequisitionCreatedPayload(
    string PurchaseRequisitionId,
    string RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate);

public sealed record PurchaseOrderReleasedPayload(
    string PurchaseOrderId,
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    decimal TotalAmount);

public sealed record PurchaseReceiptRecordedPayload(
    string PurchaseReceiptId,
    string PurchaseReceiptNo,
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    string QualityStatus,
    IReadOnlyCollection<PurchaseReceiptRecordedLinePayload>? Lines = null);

public sealed record PurchaseReceiptRecordedLinePayload(
    string LineReference,
    string SkuCode,
    string UomCode,
    string? LocationCode,
    string? LotNo,
    decimal ReceivedQuantity,
    string QualityStatus);

public sealed record PurchaseReceiptRecordedIntegrationEvent(
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
    PurchaseReceiptRecordedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record DeliveryOrderReleasedPayload(string DeliveryOrderId, string DeliveryOrderNo, string SalesOrderNo, string CustomerCode);
public sealed record AccountPayableCreatedPayload(string AccountPayableId, string PayableNo, string SourceDocumentNo, string SupplierCode, decimal Amount, string CurrencyCode);
public sealed record AccountReceivableCreatedPayload(string AccountReceivableId, string ReceivableNo, string SourceDocumentNo, string CustomerCode, decimal Amount, string CurrencyCode);
public sealed record CostCandidateCreatedPayload(string CostCandidateId, string CandidateNo, string SourceType, string SourceDocumentNo, decimal Amount, string CurrencyCode);
public sealed record JournalVoucherPostedPayload(string JournalVoucherId, string VoucherNo, DateOnly PostingDate);
