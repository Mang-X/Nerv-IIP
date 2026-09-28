using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

/// <summary>
/// MES 列表读面的受控状态枚举。
///
/// <para>每个聚合有<strong>自己</strong>的状态值域，彼此不等价：工单 10 个小写值、工序 6 个
/// PascalCase 值、领料单 7 个、成品入库单 5 个、不良记录 5 个、交接班 2 个、停机/产能影响 2 个。
/// 拼写是线上字面量的一部分（读面有的直接回显库里的字符串列，有的走 <c>Status.ToString()</c>），
/// 归一时必须原样保留，不能统一成一种大小写。</para>
///
/// <para>本处理器此前把这 10 个语义不同的状态属性和 11 个列表查询参数统一重填为同一份 29 值
/// 小写并集：对一半聚合声明了运行时永不产生的值，又让这些聚合真正会产生的值在契约里查无此项。</para>
///
/// <para>各值域的权威来源是 MES 域常量，逐个标注在下面的声明处；契约枚举与运行时值域的一致性
/// 由 <c>BusinessGatewayOpenApiTests</c> 断言。</para>
/// </summary>
public sealed class MesListDisplayOpenApiDocumentProcessor : IDocumentProcessor
{
    // 来源：MES Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs 的 WorkOrder.AllStatuses。
    // 同文件的 captured / no-requirements 是物料需求快照状态（MaterialRequirementSnapshotStatus），
    // 不是工单生命周期状态，不在本集合内。
    private static readonly string[] WorkOrderStatuses =
    [
        "created",
        "released",
        "started",
        "hold",
        "completed",
        "closed",
        "cancelled",
        "scrapped",
        "split",
        "merged",
    ];

    // 来源：MES Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs 的
    // OperationTaskLifecycleStatus。工序列表、派工、在制汇总与工单详情里的工序都走
    // `Status.ToString()`，所以线上值就是成员名本身（PascalCase）。
    private static readonly string[] OperationTaskStatuses =
    [
        "Queued",
        "InProgress",
        "Paused",
        "ScheduleInvalidated",
        "Completed",
        "Cancelled",
    ];

    // 来源：MES Mes.Domain/AggregatesModel/MaterialSupplyAggregate/MaterialIssueRequest.cs 的 *Status 常量。
    private static readonly string[] MaterialIssueRequestStatuses =
    [
        "Requested",
        "PartiallyReceived",
        "ReceiptPosting",
        "Received",
        "Cancelled",
        "ReturnRequested",
        "ReservationExpired",
    ];

    // 来源：MES Mes.Domain/AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs
    // 的 *Status 常量。
    private static readonly string[] FinishedGoodsReceiptRequestStatuses =
    [
        "Requested",
        "PartiallyPosted",
        "Posted",
        "InventoryPostingFailed",
        "Cancelled",
    ];

    // 来源：MES Mes.Domain/AggregatesModel/QualityAggregate/DefectRecord.cs 的 *Status 常量。
    private static readonly string[] DefectRecordStatuses =
    [
        "Open",
        "ReworkPending",
        "ScrapAccepted",
        "ReturnAccepted",
        "DispositionAccepted",
    ];

    // 来源：MES Mes.Domain/AggregatesModel/ShiftHandoverAggregate/ShiftHandover.cs 的
    // OpenStatus / AcceptedStatus。
    private static readonly string[] ShiftHandoverStatuses =
    [
        "Open",
        "Accepted",
    ];

    // 停机事件与产能影响读的是同一个 WorkCenterUnavailability，聚合本身没有状态列：状态由
    // 「是否已恢复」在读面派生 —— MesWorkbenchQueries / MesProductionQueries 都调
    // `WorkCenterUnavailability.DeriveStatus(x.ToUtc)`，值域见该聚合的 OpenStatus /
    // RecoveredStatus 两个常量。
    private static readonly string[] WorkCenterUnavailabilityStatuses =
    [
        "Open",
        "Recovered",
    ];

    private static readonly (string SchemaSuffix, string PropertyName, string[] Values)[] StatusProperties =
    [
        ("BusinessConsoleMesWorkOrderItem", "status", WorkOrderStatuses),
        ("BusinessConsoleMesOperationTaskItem", "status", OperationTaskStatuses),
        ("BusinessConsoleMesMaterialIssueRequestRow", "status", MaterialIssueRequestStatuses),
        ("BusinessConsoleMesDispatchTaskRow", "status", OperationTaskStatuses),
        ("BusinessConsoleMesOperationTaskRow", "status", OperationTaskStatuses),
        ("BusinessConsoleMesWipSummaryRow", "status", OperationTaskStatuses),
        ("BusinessConsoleMesRelatedQualityItemRow", "status", DefectRecordStatuses),
        ("BusinessConsoleMesReceiptRequestRow", "receiptStatus", FinishedGoodsReceiptRequestStatuses),
        ("BusinessConsoleMesDowntimeEventRow", "status", WorkCenterUnavailabilityStatuses),
        ("BusinessConsoleMesCapacityImpactRow", "status", WorkCenterUnavailabilityStatuses),
    ];

    // 每条列表路径的 status 过滤值集就是该路径背后聚合的值域，因此与上面的行属性引用同一份声明。
    private static readonly (string Path, string[] Values)[] MesListPaths =
    [
        ("/api/business-console/v1/mes/work-orders", WorkOrderStatuses),
        ("/api/business-console/v1/mes/production-plans", WorkOrderStatuses),
        ("/api/business-console/v1/mes/material-issue-requests", MaterialIssueRequestStatuses),
        ("/api/business-console/v1/mes/dispatch-tasks", OperationTaskStatuses),
        ("/api/business-console/v1/mes/operation-tasks", OperationTaskStatuses),
        ("/api/business-console/v1/mes/wip", OperationTaskStatuses),
        ("/api/business-console/v1/mes/related-quality-items", DefectRecordStatuses),
        ("/api/business-console/v1/mes/finished-goods-receipt-requests", FinishedGoodsReceiptRequestStatuses),
        ("/api/business-console/v1/mes/downtime-events", WorkCenterUnavailabilityStatuses),
        ("/api/business-console/v1/mes/shift-handovers", ShiftHandoverStatuses),
        ("/api/business-console/v1/mes/capacity-impacts", WorkCenterUnavailabilityStatuses),
    ];

    public void Process(DocumentProcessorContext context)
    {
        foreach (var (schemaSuffix, propertyName, values) in StatusProperties)
        {
            var schema = FindSchemaBySuffix(context, schemaSuffix);
            if (!schema.Properties.TryGetValue(propertyName, out var property))
            {
                throw new InvalidOperationException(
                    $"Missing MES list status property OpenAPI schema: {schemaSuffix}.{propertyName}");
            }

            ApplyStatusEnum(property, values);
        }

        foreach (var (path, values) in MesListPaths)
        {
            if (!context.Document.Paths.TryGetValue(path, out var pathItem)
                || !pathItem.TryGetValue(OpenApiOperationMethod.Get, out var operation))
            {
                throw new InvalidOperationException($"Missing MES list OpenAPI operation: GET {path}");
            }

            var statusParameter = operation.Parameters.SingleOrDefault(x =>
                x.Kind == OpenApiParameterKind.Query && string.Equals(x.Name, "status", StringComparison.Ordinal));
            if (statusParameter is null)
            {
                throw new InvalidOperationException($"Missing MES list status query parameter: GET {path}");
            }

            ApplyStatusEnum(statusParameter.Schema, values);
        }
    }

    private static JsonSchema FindSchemaBySuffix(DocumentProcessorContext context, string suffix)
    {
        var matches = context.Document.Components.Schemas
            .Where(x =>
                x.Key.EndsWith(suffix, StringComparison.Ordinal) &&
                !x.Key.StartsWith("NetCorePalExtensionsDtoResponseDataOf", StringComparison.Ordinal))
            .Select(x => x.Value)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one MES OpenAPI schema ending with {suffix}, found {matches.Length}.");
    }

    private static void ApplyStatusEnum(JsonSchema schema, string[] values)
    {
        schema.Type = JsonObjectType.String;
        schema.Format = null;
        schema.Enumeration.Clear();
        schema.EnumerationNames.Clear();
        foreach (var value in values)
        {
            schema.Enumeration.Add(value);
        }
    }
}
