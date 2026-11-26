using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesMaterialIssueRequestListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100,
    string? OperationTaskId = null);

public sealed record BusinessConsoleMesMaterialReadinessRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesMaterialReadinessResponse(
    string WorkOrderId,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<BusinessConsoleMesMaterialReadinessRow> Items,
    // 齐套核算口径（#1291）：只认线边可用 + 已备料 + 已收料，不含原料仓等其他库存。
    // MRP 用的是全厂库存口径，两个数字不同是正常的——读面必须显式标注，不能让用户以为系统自相矛盾。
    string? ReadinessScope = null,
    DateTimeOffset? SnapshotCapturedAtUtc = null);

public sealed record BusinessConsoleMesMaterialReadinessRow(
    string MaterialId,
    string? MaterialLotId,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal RequestedQuantity,
    decimal StagedQuantity,
    decimal ReceivedQuantity,
    decimal ShortageQuantity,
    string Status,
    // 缺口卡在哪个环节：none / awaitingPreparation（还没发起领料）/ awaitingDelivery（仓库在配）。
    string? ShortageStage = null,
    IReadOnlyCollection<string>? SubstituteMaterialIds = null,
    string? UomCode = null,
    DateTimeOffset? ExpectedAvailableAtUtc = null,
    string? ExpectedAvailabilitySource = null);

// 工单可入库产出批次（MAN-445/#799）：从 MES OutputLotGenealogies 权威表列出当前有效的产出批次，供 Console
// 完工入库选择真实 producedLotNo。读权限 MesReceiptsRead 与完工入库列表一致，避免入库操作员因缺 reporting.read
// 而在选批次时 403。
public sealed record BusinessConsoleMesCreateMaterialIssueRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? OperationTaskId,
    string MaterialId,
    string UomCode,
    decimal? Quantity,
    IReadOnlyCollection<string>? MaterialIds,
    string IdempotencyKey,
    bool IsSupplementary = false,
    string? OriginalMaterialIssueRequestNo = null);

public sealed record BusinessConsoleMesMaterialIssueRequestListResponse(
    IReadOnlyCollection<BusinessConsoleMesMaterialIssueRequestRow> Items,
    int Total,
    int SupplementaryCount = 0);

public sealed record BusinessConsoleMesMaterialIssueRequestRow(
    string RequestId,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string UomCode,
    string? MaterialLotId,
    decimal RequestedQuantity,
    decimal ReceivedQuantity,
    decimal ConsumedQuantity,
    string Status,
    string? WmsRequestId,
    DateTimeOffset RequestedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? MaterialCode = null,
    bool IsSupplementary = false,
    string? OriginalMaterialIssueRequestNo = null,
    string? SubstitutedMaterialId = null);

public sealed record BusinessConsoleMesMaterialIssueRequestDetailRequest(
    [property: RouteParam] string RequestId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesConfirmLineSideReceiptRequest(
    [property: RouteParam] string RequestId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? MaterialLotId,
    decimal? ReceivedQuantity,
    IReadOnlyCollection<string>? EvidenceFileIds,
    [property: StringLength(150, MinimumLength = 1)] string? IdempotencyKey = null);

public sealed record BusinessConsoleMesReturnLineSideMaterialRequest(
    [property: RouteParam] string RequestId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    DateTimeOffset? ReturnedAtUtc,
    decimal ReturnedQuantity,
    [property: JsonRequired, Required] string IdempotencyKey);
