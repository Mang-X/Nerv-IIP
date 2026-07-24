using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleWmsInboundLineInput(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string StagingLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);

public sealed record BusinessConsoleWmsOutboundLineInput(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal RequestedQuantity,
    string PickLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleCreateWmsInboundOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string InboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SiteCode,
    IReadOnlyCollection<BusinessConsoleWmsInboundLineInput> Lines);

public sealed record BusinessConsoleCreateWmsInboundOrderResponse(string InboundOrderId);

public sealed record BusinessConsoleCreateWmsPutawayTaskRequest(
    [property: RouteParam] string InboundOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string TaskNo,
    string LineNo,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity);

public sealed record BusinessConsoleCreateWmsWarehouseTaskResponse(string WarehouseTaskId);

public sealed record BusinessConsoleCompleteWmsInboundOrderRequest(
    [property: RouteParam] string InboundOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string IdempotencyKey,
    IReadOnlyCollection<BusinessConsoleWmsInboundLineCaptureInput>? Lines = null);

public sealed record BusinessConsoleWmsInboundLineCaptureInput(
    string LineNo,
    string? LotNo,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate);

public sealed record BusinessConsoleCompleteWmsMovementResponse(string? RequestId, string? InventoryMovementId);

public sealed record BusinessConsoleCreateWmsOutboundOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string OutboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SiteCode,
    IReadOnlyCollection<BusinessConsoleWmsOutboundLineInput> Lines);

public sealed record BusinessConsoleCreateWmsOutboundOrderResponse(string OutboundOrderId);

public sealed record BusinessConsoleCreateWmsPickingTaskRequest(
    [property: RouteParam] string OutboundOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string TaskNo,
    string LineNo,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity);

public sealed record BusinessConsoleCompleteWmsOutboundOrderRequest(
    [property: RouteParam] string OutboundOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string PackReviewNo,
    bool Passed,
    string IdempotencyKey);

public sealed record BusinessConsoleRetryWmsOutboundInventoryPostingRequest(
    [property: RouteParam] string OutboundOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string IdempotencyKey);

public sealed record BusinessConsoleCreateWmsCountExecutionRequest(
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal ExpectedQuantity);

public sealed record BusinessConsoleCreateWmsCountExecutionResponse(string CountExecutionId);

public sealed record BusinessConsoleCompleteWmsCountExecutionRequest(
    [property: RouteParam] string CountExecutionId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    decimal CountedQuantity,
    string IdempotencyKey);

public sealed record BusinessConsoleDispatchWmsWcsTaskRequest(
    [property: RouteParam] string WarehouseTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string AdapterType,
    string ExternalTaskId,
    string PayloadJson);

public sealed record BusinessConsoleDispatchWmsWcsTaskResponse(string WcsTaskId);

public sealed record BusinessConsoleFailWmsWcsTaskRequest(
    [property: RouteParam] string ExternalTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string FailureCode,
    string FailureMessage);

public sealed record BusinessConsoleCompleteWmsWcsTaskRequest(
    [property: RouteParam] string ExternalTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string CompletionPayloadJson);

public sealed record BusinessConsoleWmsListRequest(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null);

public sealed record BusinessConsoleWmsInboundOrderListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null);

public sealed record BusinessConsoleWmsInboundOrderListResponse(
    IReadOnlyCollection<BusinessConsoleWmsInboundOrderItem> Items,
    int Total,
    BusinessConsoleWmsInventoryContext? InventoryContext,
    string SourceStatus);

public sealed record BusinessConsoleWmsInboundOrderItem(
    string InboundOrderId,
    string InboundOrderNo,
    string Status,
    DateTime CreatedAtUtc,
    // 单据级派生质检状态（聚合全部收货行含免检；无行为空串）+ 上架放行判据，
    // 供列表质检状态标与上架门禁；避免前端按分页门禁行跨页聚合出错。
    string QualityGateStatus,
    bool IsReleasedForPutaway);

public sealed record BusinessConsoleWmsOutboundOrderListResponse(
    IReadOnlyCollection<BusinessConsoleWmsOutboundOrderItem> Items,
    int Total);

public sealed record BusinessConsoleWmsOutboundOrderItem(
    string OutboundOrderId,
    string OutboundOrderNo,
    string Status,
    string SiteCode,
    string InventoryPostingStatus,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyCollection<BusinessConsoleWmsOutboundOrderLineItem> Lines,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record BusinessConsoleWmsOutboundOrderLineItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal RequestedQuantity,
    decimal IssuedQuantity,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    string InventoryPostingStatus,
    string? FailureCode,
    string? FailureMessage);

public sealed record BusinessConsoleWmsWarehouseTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? LocationCode,
    string? OperatorUserId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null);

public sealed record BusinessConsoleWmsWarehouseTaskListResponse(
    IReadOnlyCollection<BusinessConsoleWmsWarehouseTaskItem> Items,
    int Total);

public sealed record BusinessConsoleWmsWarehouseTaskItem(
    string WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string TaskType,
    string TaskNo,
    string SourceOrderNo,
    string SourceOrderLineNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string FromLocationCode,
    string ToLocationCode,
    decimal PlannedQuantity,
    decimal ExecutedQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record BusinessConsoleWmsCountExecutionListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? LocationCode,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null);

public sealed record BusinessConsoleWmsCountExecutionListResponse(
    IReadOnlyCollection<BusinessConsoleWmsCountExecutionItem> Items,
    int Total);

public sealed record BusinessConsoleWmsCountExecutionItem(
    string CountExecutionId,
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? VarianceQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record BusinessConsoleWmsWcsTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ExternalTaskId,
    string? WarehouseTaskId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    bool? Failed = null,
    string? Keyword = null);

public sealed record BusinessConsoleWmsWcsTaskListResponse(
    IReadOnlyCollection<BusinessConsoleWmsWcsTaskItem> Items,
    int Total);

public sealed record BusinessConsoleWmsWcsTaskItem(
    string WcsTaskId,
    string OrganizationId,
    string EnvironmentId,
    string WarehouseTaskId,
    string AdapterType,
    string ExternalTaskId,
    string Status,
    int AttemptCount,
    string? FailureCode,
    string? FailureMessage,
    DateTime DispatchedAtUtc,
    DateTime? FailedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record BusinessConsoleWmsInventoryContext(
    string Source,
    string Status,
    string? PermissionCode,
    string? Reason,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    decimal? OnHandQuantity,
    decimal? ReservedQuantity,
    decimal? AvailableQuantity,
    IReadOnlyCollection<BusinessConsoleInventoryAvailabilityLineResponse> Items);

public sealed record BusinessConsoleWmsReceivingQualityGateListRequest(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? GateStatus = null,
    string? Keyword = null,
    // true 时返回全部收货行（含免检），供 PDA 收货明细展示/采集免检行批号效期与「免检」标；
    // 默认 false 保持质检工作清单语义（仅需检行）。
    bool IncludeNotRequired = false,
    // 精确单号过滤：PDA 收货明细按单取完整行，避免 keyword 跨单串扰。
    string? InboundOrderNo = null);

public sealed record BusinessConsoleWmsReceivingQualityGateListResponse(
    IReadOnlyCollection<BusinessConsoleWmsReceivingQualityGateItem> Items,
    int Total);

public sealed record BusinessConsoleWmsReceivingQualityGateItem(
    string InboundOrderId,
    string InboundOrderLineId,
    string OrganizationId,
    string EnvironmentId,
    string InboundOrderNo,
    string InboundOrderStatus,
    string SiteCode,
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string StagingLocationCode,
    string? LotNo,
    string? SerialNo,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    string QualityStatus,
    string QualityGateStatus,
    string? InspectionRecordId,
    string? QualityDispositionReason,
    string OwnerType,
    string? OwnerId,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleWmsSupplierReturnListResponse(
    IReadOnlyCollection<BusinessConsoleWmsSupplierReturnItem> Items,
    int Total);

public sealed record BusinessConsoleWmsSupplierReturnItem(
    string SupplierReturnRequestId,
    string OrganizationId,
    string EnvironmentId,
    string SupplierReturnNo,
    string InboundOrderNo,
    string InboundOrderLineNo,
    string InspectionRecordId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    string DispositionType,
    string? DispositionReason,
    string Status,
    DateTime CreatedAtUtc);
