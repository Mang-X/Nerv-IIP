using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

[JsonConverter(typeof(BusinessConsoleMesProductionStatisticsDimensionJsonConverter))]
public enum BusinessConsoleMesProductionStatisticsDimension
{
    Day,
    Shift,
    WorkCenter,
    Sku,
}

public sealed class BusinessConsoleMesProductionStatisticsDimensionJsonConverter()
    : JsonStringEnumConverter<BusinessConsoleMesProductionStatisticsDimension>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(BusinessConsoleMesProductionStatisticsResolutionStatusJsonConverter))]
public enum BusinessConsoleMesProductionStatisticsResolutionStatus
{
    Resolved,
    Degraded,
}

public sealed class BusinessConsoleMesProductionStatisticsResolutionStatusJsonConverter()
    : JsonStringEnumConverter<BusinessConsoleMesProductionStatisticsResolutionStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(BusinessConsoleMesProductionStatisticsDegradedReasonJsonConverter))]
public enum BusinessConsoleMesProductionStatisticsDegradedReason
{
    HistoricalDimensionLegacyUnresolved,
    HistoricalTimezoneMissing,
    HistoricalTimezoneInvalid,
    HistoricalShiftDefinitionMissing,
    HistoricalShiftDefinitionInvalid,
    HistoricalReportOutsideShiftWindow,
    HistoricalLocalTimeInvalid,
    HistoricalLocalTimeAmbiguous,
    HistoricalDimensionSnapshotDegraded,
    WorkCenterMissing,
    NonPositiveTotalOutput,
    DeviceMissing,
}

public sealed class BusinessConsoleMesProductionStatisticsDegradedReasonJsonConverter()
    : JsonStringEnumConverter<BusinessConsoleMesProductionStatisticsDegradedReason>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record BusinessConsoleMesProductionStatisticsRequest(
    string OrganizationId,
    string EnvironmentId,
    BusinessConsoleMesProductionStatisticsDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateOnly? BusinessDate = null,
    string? ShiftCode = null,
    string? WorkCenterId = null,
    string? SkuId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesProductionStatisticsResponse(
    [property: JsonRequired, Required] string OrganizationId,
    [property: JsonRequired, Required] string EnvironmentId,
    [property: JsonRequired, Required] BusinessConsoleMesProductionStatisticsDimension Dimension,
    [property: JsonRequired, Required] DateTimeOffset WindowStartUtc,
    [property: JsonRequired, Required] DateTimeOffset WindowEndUtc,
    [property: JsonRequired, Required] IReadOnlyCollection<BusinessConsoleMesProductionStatisticsBucket> Items,
    [property: JsonRequired, Required] int TotalCount,
    [property: JsonRequired, Required] int Skip,
    [property: JsonRequired, Required] int Take);

public sealed record BusinessConsoleMesProductionStatisticsBucket(
    [property: JsonRequired, Required] BusinessConsoleMesProductionStatisticsDimension Dimension,
    string? DimensionValue,
    DateOnly? BusinessDate,
    string? ShiftCode,
    string? WorkCenterId,
    string? SkuId,
    [property: JsonRequired, Required] decimal GoodQuantity,
    [property: JsonRequired, Required] decimal ScrapQuantity,
    [property: JsonRequired, Required] decimal ReworkQuantity,
    [property: JsonRequired, Required] decimal TotalOutputQuantity,
    decimal? GoodRate,
    decimal? ScrapRate,
    decimal? ReworkRate,
    [property: JsonRequired, Required] int ProductionReportCount,
    [property: JsonRequired, Required] BusinessConsoleMesProductionStatisticsResolutionStatus ResolutionStatus,
    [property: JsonRequired, Required] IReadOnlyCollection<BusinessConsoleMesProductionStatisticsDegradedReason> DegradedReasons);

public sealed record BusinessConsoleRecordProductionReportRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    bool CompletesOperation,
    DateTimeOffset ReportedAtUtc,
    string IdempotencyKey,
    string ScopeKind,
    string ScopeId,
    IReadOnlyCollection<BusinessConsoleConsumedMaterialLotInput>? ConsumedMaterialLots = null,
    decimal ReworkQuantity = 0m,
    string? ScrapReasonCode = null,
    string? DefectRecordNo = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string SerialTrackingPolicy = "none",
    IReadOnlyCollection<string>? SerialNumbers = null,
    string? LabelTemplateId = null);

public sealed record BusinessConsoleConsumedMaterialLotInput(
    string MaterialId,
    string MaterialLotId,
    decimal ConsumedQuantity,
    string MaterialIssueRequestNo);

public sealed record BusinessConsoleRecordProductionReportResponse(
    string ProductionReportId,
    string ReportNo,
    IReadOnlyCollection<string> SerialNumbers,
    BusinessConsoleOperationReceipt? OperationReceipt = null,
    string? PrintBatchId = null,
    string? PrintStatus = null,
    bool PrintingPreparationPending = false);

public sealed record BusinessConsoleMesReverseProductionReportRequest(
    [property: RouteParam] string ReportNo,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason,
    DateTimeOffset? ReversedAtUtc,
    string? IdempotencyKey);

public sealed record BusinessConsoleMesReverseProductionReportResponse(
    string ProductionReportId,
    string ReportNo,
    string OriginalReportNo);

public sealed record BusinessConsoleMesProductionReportListResponse(
    IReadOnlyCollection<BusinessConsoleMesProductionReportRow> Items,
    int Total);

public sealed record BusinessConsoleMesProductionReportDetailRequest(
    [property: RouteParam] string ReportNo,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesProductionReportDetailResponse(
    BusinessConsoleMesProductionReportDetail Report,
    IReadOnlyCollection<BusinessConsoleMesConsumedMaterialLot> ConsumedMaterialLots,
    IReadOnlyCollection<BusinessConsoleMesLaborAllocation> LaborAllocations);

public sealed record BusinessConsoleMesProductionReportDetail(
    string ProductionReportId,
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset ReportedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? ScrapReasonCode = null,
    string? DefectRecordNo = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string? ReversedReportNo = null,
    string? ReversalReason = null,
    string? InventoryPostingFailureCode = null,
    string? InventoryPostingFailureMessage = null,
    DateTimeOffset? InventoryPostingFailedAtUtc = null,
    string? WorkOrderStatus = null,
    string? ReversalReportNo = null,
    [property: Description("对应工序完成后冻结的累计实际人工工时，单位为小时；工序未完成或冲销后重新打开时为 null。")]
    decimal? OperationActualLaborHours = null,
    [property: Description("对应工序完成后冻结的累计实际机器工时，单位为小时；工序未完成或冲销后重新打开时为 null。")]
    decimal? OperationActualMachineHours = null,
    [property: Description("提交本条报工的操作人引用；升级前的历史报工为 null。")]
    string? ReportedBy = null,
    IReadOnlyCollection<string>? SerialNumbers = null,
    [property: Description("当前组织/环境成员账号的 IAM 显示名；账号无匹配或无显示名时为 null。")]
    string? ReportedByDisplayName = null);

public sealed record BusinessConsoleMesConsumedMaterialLot(
    string MaterialId,
    string MaterialLotId,
    decimal ConsumedQuantity,
    string UomCode,
    string MaterialIssueRequestNo);

public sealed record BusinessConsoleMesProductionReportRow(
    string ProductionReportId,
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset ReportedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    // 冲销互链(MAN-444/#798):冲销行携带被冲销的原报工号与冲销原因,供 Console 展示负向记录标记
    // 与「原单 ⇄ 冲销单」双向高亮。MES 事实层 ProductionReportFact 已有这两个字段,facade 直接透传。
    string? ReversedReportNo = null,
    string? ReversalReason = null,
    // 报工所属工单状态,供 Console 冲销按钮分级(已关闭工单前端禁用,与后端拒绝构成双层拦截,MAN-444/#798)。
    string? WorkOrderStatus = null,
    // 冲销本报工的负向记录单号(服务端逐行反查):非空即"已冲销",供 Console 跨分页稳定判定已冲销与
    // 原单→冲销单互链,不再从当前页推断(MAN-444/#798 review)。
    string? ReversalReportNo = null,
    // 产出批次 / 序列号(MES 事实层 ProductionReportFact 已有,facade 直接透传):完工入库创建端点强制引用
    // MES 已生成的产出批次(见 CreateFinishedGoodsReceiptRequestCommandHandler),Console 据此让操作员从工单
    // 真实报工产出中选择 producedLotNo,而非前端伪造(MAN-445/#799 review)。
    string? ProducedLotNo = null,
    string? SerialNo = null,
    [property: Description("对应工序完成后冻结的累计实际人工工时，单位为小时；工序未完成或冲销后重新打开时为 null。")]
    decimal? OperationActualLaborHours = null,
    [property: Description("对应工序完成后冻结的累计实际机器工时，单位为小时；工序未完成或冲销后重新打开时为 null。")]
    decimal? OperationActualMachineHours = null,
    [property: Description("提交本条报工的操作人引用；升级前的历史报工为 null。")]
    string? ReportedBy = null,
    IReadOnlyCollection<string>? SerialNumbers = null,
    [property: Description("当前组织/环境成员账号的 IAM 显示名；账号无匹配或无显示名时为 null。")]
    string? ReportedByDisplayName = null);
