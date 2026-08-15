using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Wms;

public static class WmsIntegrationEventTypes
{
    public const string InboundOrderCompleted = "wms.InboundOrderCompleted";
    public const string OutboundOrderCompleted = "wms.OutboundOrderCompleted";
    public const string OutboundOrderCancelled = "wms.OutboundOrderCancelled";
    public const string OutboundOrderRequested = "wms.OutboundOrderRequested";
    public const string CountExecutionCompleted = "wms.CountExecutionCompleted";
    public const string WcsTaskDispatched = "wms.WcsTaskDispatched";
    public const string WcsTaskFailed = "wms.WcsTaskFailed";
    public const string WcsTaskRetryExhausted = "wms.WcsTaskRetryExhausted";
    public const string WcsTaskCompleted = "wms.WcsTaskCompleted";
    public const string WcsTaskCancelled = "wms.WcsTaskCancelled";
    public const string MaterialIssueOutboundPrepared = "wms.MaterialIssueOutboundPrepared";
}

public static class WmsIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class WmsIntegrationEventSources
{
    public const string BusinessWms = "business-wms";
    public const string BusinessErp = "business-erp";
}

public static class WmsSourceDocumentTypes
{
    public const string MesMaterialIssueRequest = "mes-material-issue-request";
    public const string PurchaseReceipt = "purchase-receipt";
    public const string PurchaseReceiptReturn = "purchase-receipt-return";
    public const string SalesReturnRma = "sales-return-rma";

    /// <summary>
    /// ERP 发货单派生的出库单。
    ///
    /// **这一项是发货链的对账键，不是装饰。** 出库完成事件的 <c>PublicReference</c>
    /// 只在源单据类型等于本值时才回填发货单号，ERP 应收消费者再按发货单号反查
    /// （<c>WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable</c>）。
    /// 写错一个字面量链路不会报错：消费者查不到就 <c>LogDebug</c> 后 return，
    /// 不进死信、不告警，表现为「库存扣了、出库单已完成、发货单永停 released、应收与凭证不出现」。
    /// 因此本常量是唯一来源——运行期入口、事件转换器与世界观种子一律引用它，不许再写字面量（#1374）。
    /// </summary>
    public const string DeliveryOrder = "erp-delivery-order";
}

public static class WmsReceivingQualityStatuses
{
    public const string Quality = "quality";
    public const string InspectionRequired = "inspection-required";
    public const string QualityInspectionRequired = "quality-inspection-required";
    public const string PendingQualityCheck = "pending-quality-check";
    public const string Exempt = "exempt";
    public const string InspectionExempt = "inspection-exempt";
    public const string SkipInspection = "skip-inspection";
    public const string SamplingSkip = "sampling-skip";
    public const string SamplingSkipped = "sampling-skipped";
    public const string Unrestricted = "unrestricted";
    public const string Qualified = "qualified";

    public static readonly IReadOnlyCollection<string> InspectionSkippedStatuses =
    [
        Exempt,
        InspectionExempt,
        SkipInspection,
        SamplingSkip,
        SamplingSkipped,
        Unrestricted,
        Qualified,
    ];

    private static readonly HashSet<string> InspectionSkippedLookup = new(InspectionSkippedStatuses, StringComparer.OrdinalIgnoreCase);

    public static bool ShouldSkipInspection(string? qualityStatus)
    {
        return !string.IsNullOrWhiteSpace(qualityStatus) && InspectionSkippedLookup.Contains(qualityStatus.Trim());
    }

    public static bool RequiresInspection(string? qualityStatus)
    {
        return !ShouldSkipInspection(qualityStatus);
    }
}

public sealed record WmsIntegrationEvent(
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
    WmsIntegrationPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WmsIntegrationPayload(
    string PublicReference,
    string? LineReference,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    decimal? Quantity,
    string? Status,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    IReadOnlyCollection<WmsIntegrationPayloadLine>? Lines = null,
    string? SourceDocumentType = null,
    string? SourceDocumentId = null,
    string? AdapterType = null);

public sealed record WmsIntegrationPayloadLine(
    string LineReference,
    string SkuCode,
    string UomCode,
    string? SiteCode,
    string? LocationCode,
    decimal Quantity,
    string? Status);

public sealed record WmsOutboundOrderRequestedIntegrationEvent(
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
    WmsOutboundOrderRequestedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WmsOutboundOrderRequestedPayload(
    string DeliveryOrderNo,
    string SalesOrderNo,
    string CustomerCode,
    string? SiteCode,
    IReadOnlyCollection<WmsOutboundOrderRequestedLine> Lines);

public sealed record WmsOutboundOrderRequestedLine(
    string SourceLineNo,
    string SkuCode,
    string UomCode,
    string LocationCode,
    string? LotNo,
    decimal Quantity);

/// <summary>
/// Warehouse acknowledgement for a MES material issue request: the outbound document (and the first
/// picking task) that MES can quote back to the operator as 出库单.
/// </summary>
public sealed record WmsMaterialIssueOutboundPreparedIntegrationEvent(
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
    WmsMaterialIssueOutboundPreparedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record WmsMaterialIssueOutboundPreparedPayload(
    string MaterialIssueRequestNo,
    string OutboundOrderNo,
    string? PickingTaskNo,
    string SiteCode,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    DateTimeOffset PreparedAtUtc);
