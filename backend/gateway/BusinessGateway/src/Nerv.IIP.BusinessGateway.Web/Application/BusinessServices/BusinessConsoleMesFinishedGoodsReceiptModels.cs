using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest(
    [property: RouteParam] string RequestNo,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string IdempotencyKey);

public sealed record BusinessConsoleMesReceivableProducedLotsRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesReceivableProducedLotListResponse(
    IReadOnlyCollection<BusinessConsoleMesReceivableProducedLotRow> Items);

public sealed record BusinessConsoleMesReceivableProducedLotRow(
    string ProducedLotNo,
    string ReportNo,
    string OperationTaskId,
    decimal Quantity,
    DateTimeOffset CreatedAtUtc,
    // 剩余可入库量（批次产量 − 已有非取消入库申请）：读面已过滤耗尽批次，Console 据此提示剩余并按剩余限制数量。
    decimal RemainingQuantity,
    string? SerialNo = null);

public sealed record BusinessConsoleMesReceiptRequestListResponse(
    IReadOnlyCollection<BusinessConsoleMesReceiptRequestRow> Items,
    int Total);

public sealed record BusinessConsoleMesReceiptRequestRow(
    string ReceiptRequestId,
    string RequestNo,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    decimal? UnitCost,
    string ReceiptStatus,
    DateTimeOffset RequestedAtUtc,
    string? WorkOrderNo = null,
    string? SkuCode = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string? PostedInventoryMovementId = null,
    DateTimeOffset? PostedAtUtc = null,
    string? InventoryPostingFailureCode = null,
    string? InventoryPostingFailureMessage = null,
    DateTimeOffset? InventoryPostingFailedAtUtc = null,
    decimal? PostedQuantity = null,
    decimal? RemainingQuantity = null,
    BusinessConsoleMesReceiptCostCapitalizationProgress? CostCapitalization = null);

/// <summary>
/// 入库单等待成本归集时 ERP 侧的归集进度（#3728）。ERP 要「工单已完工 + 报工成本到齐 + 物料过账到齐」
/// 才发布单位成本；任何一项不前进（例如报工事件进了死信），入库单就停在 Requested。
/// 仅对「Requested 且尚无单位成本」的行、且调用者有 ERP 财务读权限时填充。
/// </summary>
public sealed record BusinessConsoleMesReceiptCostCapitalizationProgress(
    bool WorkOrderCompleted,
    int ReceivedReportCount,
    int ExpectedReportCount,
    int ReceivedMaterialMovementCount,
    int ExpectedMaterialMovementCount,
    bool CapitalizationPublished);

public sealed record BusinessConsoleMesFinishedGoodsInventoryLinkRequest(
    [property: RouteParam] string RequestNo,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: QueryParam] string? WorkOrderId = null);

public sealed record BusinessConsoleMesFinishedGoodsInventoryLinkResponse(
    string LinkStatus,
    string RequestNo,
    string WorkOrderId,
    string? WorkOrderNo,
    string SkuId,
    string? SkuCode,
    string? ProducedLotNo,
    string? SerialNo,
    decimal RequestedQuantity,
    decimal? PostedQuantity,
    decimal? RemainingQuantity,
    string ReceiptStatus,
    string? PostedInventoryMovementId,
    DateTimeOffset? PostedAtUtc,
    string? InventoryPostingFailureCode,
    string? InventoryPostingFailureMessage,
    DateTimeOffset? InventoryPostingFailedAtUtc,
    string SourceService,
    string SourceDocumentId,
    string SourceDocumentLineId,
    bool IsInventoryLinkEstablished,
    IReadOnlyCollection<BusinessConsoleInventorySourceMovement> Movements,
    IReadOnlyCollection<BusinessConsoleInventorySourceBalance> Balances);

public sealed record BusinessConsoleMesCreateReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc,
    string IdempotencyKey,
    string? ProducedLotNo = null,
    string? SerialNo = null);

public sealed record BusinessConsoleMesCreateReceiptResponse(string FinishedGoodsReceiptRequestId, string RequestNo);
