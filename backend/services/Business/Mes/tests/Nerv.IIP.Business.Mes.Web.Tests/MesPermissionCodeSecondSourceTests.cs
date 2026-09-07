using System.Reflection;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// 权限码取值的「第二源」。
//
// ⚠️ 本文件里的权限码一律写成裸字面量，**刻意不引用 MesPermissionCodes.* 常量**。这不是待清理的
// 技术债，是这些断言唯一的鉴别力来源，动它等于删掉防线，而且删完没有任何门禁会红：
//
//   * 若把右侧换成常量引用，断言两侧就走同一个符号，成为同义反复。此时把某个端点的权限码
//     换成同族但不同值的另一个常量（例如 MesFoundationRead → MesOverviewRead），断言照样通过。
//   * containment 类门禁（scripts/verify-permission-code-producer-consistency.ps1 的
//     `Gateway ⊆ IAM`）也抓不到这种变异：同族两个值通常**都**在 IAM 种子里，
//     变异前后 containment 同样成立。#3094 把值改成常量引用之后，「值写错」在编译期已不可能，
//     剩下的唯一错误形态就是**选错常量**，而选错的那个通常也合法。
//
// 结论：这一族缺陷只能靠一份独立于常量的取值转写来抓。实证见 issue #3172 与 #3094：
// IIoT 的同型变异因为 IndustrialTelemetryEndpointContractTests 里的裸字面量被杀掉，
// 而 Wms 的 `x.PermissionCode == WmsPermissionCodes.ReceiptsRead` 是同义反复，变异存活。
//
// 修改本文件前先读 #3172。新增端点时在这里补一行裸字面量，不要引用常量。
public sealed class MesPermissionCodeSecondSourceTests
{
    [Fact]
    public void Mes_endpoint_permission_codes_match_their_literal_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getBusinessMesFoundationReadinessArea"] = "business.mes.foundation.read", // GET /api/business/v1/mes/foundation-readiness/{areaCode}
            ["getBusinessMesOverview"] = "business.mes.overview.read", // GET /api/business/v1/mes/overview
            ["listBusinessMesProductionPlans"] = "business.mes.plans.read", // GET /api/business/v1/mes/production-plans
            ["getBusinessMesProductionPlanReadiness"] = "business.mes.plans.read", // GET /api/business/v1/mes/production-plans/{productionPlanId}/readiness
            ["convertBusinessMesPlanToWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/production-plans/{productionPlanId}/work-orders
            ["runBusinessMesSchedule"] = "business.mes.schedules.manage", // POST /api/business/v1/mes/schedules/run
            ["listBusinessMesScheduleResults"] = "business.mes.schedules.read", // GET /api/business/v1/mes/schedules
            ["createBusinessMesRushWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/rush
            ["listBusinessMesWorkOrders"] = "business.mes.work-orders.read", // GET /api/business/v1/mes/work-orders
            ["getBusinessMesWorkOrderDetail"] = "business.mes.work-orders.read", // GET /api/business/v1/mes/work-orders/{workOrderId}
            ["splitBusinessMesWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/split
            ["mergeBusinessMesWorkOrders"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/merge
            ["getBusinessMesWorkOrderTransformation"] = "business.mes.work-orders.read", // GET /api/business/v1/mes/work-order-transformations/{transformationId}
            ["releaseBusinessMesWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/release
            ["closeBusinessMesWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/close
            ["holdBusinessMesWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/hold
            ["cancelBusinessMesWorkOrder"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/cancel
            ["recordBusinessMesEngineeringChangeDecision"] = "business.mes.work-orders.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/engineering-change-decisions
            ["forceReleaseBusinessMesQualityHold"] = "business.mes.quality.write", // POST /api/business/v1/mes/quality-holds/{sourceDocumentId}/force-release
            ["getBusinessMesQualityHoldTimeline"] = "business.mes.quality.read", // GET /api/business/v1/mes/quality-holds/{sourceDocumentId}/timeline
            ["getBusinessMesMaterialReadiness"] = "business.mes.materials.read", // GET /api/business/v1/mes/work-orders/{workOrderId}/material-readiness
            ["createBusinessMesMaterialIssueRequest"] = "business.mes.materials.manage", // POST /api/business/v1/mes/work-orders/{workOrderId}/material-issue-requests
            ["listBusinessMesMaterialIssueRequests"] = "business.mes.materials.read", // GET /api/business/v1/mes/material-issue-requests
            ["getBusinessMesMaterialIssueRequest"] = "business.mes.materials.read", // GET /api/business/v1/mes/material-issue-requests/{requestId}
            ["prevalidateBusinessMesMaterialScan"] = "business.mes.materials.read", // POST /api/business/v1/mes/material-scan-prevalidation
            ["prevalidateBusinessMesContextScan"] = "business.mes.operations.read", // POST /api/business/v1/mes/context-scan-prevalidation
            ["confirmBusinessMesLineSideMaterialReceipt"] = "business.mes.materials.manage", // POST /api/business/v1/mes/material-issue-requests/{requestId}/line-side-receipts
            ["returnBusinessMesLineSideMaterial"] = "business.mes.materials.manage", // POST /api/business/v1/mes/material-issue-requests/{requestId}/line-side-returns
            ["listBusinessMesDispatchTasks"] = "business.mes.dispatch.read", // GET /api/business/v1/mes/dispatch-tasks
            ["assignBusinessMesDispatchTask"] = "business.mes.dispatch.manage", // POST /api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign
            ["listBusinessMesOperationTasks"] = "business.mes.operations.read", // GET /api/business/v1/mes/operation-tasks
            ["claimBusinessMesOperationTask"] = "business.mes.operations.manage", // POST /api/business/v1/mes/operation-tasks/{operationTaskId}/claim
            ["listBusinessMesReportableOperationTasks"] = "business.mes.reporting.read", // GET /api/business/v1/mes/reportable-operation-tasks
            ["startBusinessMesOperationTask"] = "business.mes.operations.manage", // POST /api/business/v1/mes/operation-tasks/{operationTaskId}/start
            ["authorizeAndStartBusinessMesOperationTask"] = "business.mes.operations.manage", // POST /api/business/v1/mes/operation-tasks/{operationTaskId}/authorize-start
            ["pauseBusinessMesOperationTask"] = "business.mes.operations.manage", // POST /api/business/v1/mes/operation-tasks/{operationTaskId}/pause
            ["resumeBusinessMesOperationTask"] = "business.mes.operations.manage", // POST /api/business/v1/mes/operation-tasks/{operationTaskId}/resume
            ["completeBusinessMesOperationTask"] = "business.mes.operations.manage", // POST /api/business/v1/mes/operation-tasks/{operationTaskId}/complete
            ["getBusinessMesWipSummary"] = "business.mes.operations.read", // GET /api/business/v1/mes/wip
            ["recordBusinessMesProductionReport"] = "business.mes.reporting.write", // POST /api/business/v1/mes/production-reports
            ["listBusinessMesProductionReports"] = "business.mes.reporting.read", // GET /api/business/v1/mes/production-reports
            ["queryBusinessMesProductionStatistics"] = "business.mes.reporting.read", // GET /api/business/v1/mes/production-statistics
            ["getBusinessMesProductionReport"] = "business.mes.reporting.read", // GET /api/business/v1/mes/production-reports/{reportNo}
            ["reverseBusinessMesProductionReport"] = "business.mes.reporting.write", // POST /api/business/v1/mes/production-reports/{reportNo}/reverse
            ["listBusinessMesTelemetryProductionReportCandidates"] = "business.mes.reporting.read", // GET /api/business/v1/mes/telemetry-production-report-candidates
            ["getBusinessMesTelemetryProductionReportCandidate"] = "business.mes.reporting.read", // GET /api/business/v1/mes/telemetry-production-report-candidates/{candidateId}
            ["promoteBusinessMesTelemetryProductionReportCandidate"] = "business.mes.reporting.write", // POST /api/business/v1/mes/telemetry-production-report-candidates/{candidateId}/promote
            ["dismissBusinessMesTelemetryProductionReportCandidate"] = "business.mes.reporting.write", // POST /api/business/v1/mes/telemetry-production-report-candidates/{candidateId}/dismiss
            ["recordBusinessMesDefect"] = "business.mes.quality.write", // POST /api/business/v1/mes/defects
            ["listBusinessMesRelatedQualityItems"] = "business.mes.quality.read", // GET /api/business/v1/mes/related-quality-items
            ["createBusinessMesFinishedGoodsReceiptRequest"] = "business.mes.receipts.manage", // POST /api/business/v1/mes/finished-goods-receipt-requests
            ["listBusinessMesFinishedGoodsReceiptRequests"] = "business.mes.receipts.read", // GET /api/business/v1/mes/finished-goods-receipt-requests
            ["listBusinessMesReceivableProducedLots"] = "business.mes.receipts.read", // GET /api/business/v1/mes/work-orders/{workOrderId}/produced-lots
            ["retryBusinessMesFinishedGoodsReceiptInventoryPosting"] = "business.mes.receipts.manage", // POST /api/business/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-posting/retry
            ["listBusinessMesDowntimeEvents"] = "business.mes.downtime.read", // GET /api/business/v1/mes/downtime-events
            ["recordBusinessMesDowntimeEvent"] = "business.mes.downtime.manage", // POST /api/business/v1/mes/downtime-events
            ["confirmBusinessMesDowntimeRecovery"] = "business.mes.downtime.manage", // POST /api/business/v1/mes/downtime-events/{downtimeEventId}/recover
            ["startBusinessMesChangeover"] = "business.mes.operations.manage", // POST /api/business/v1/mes/changeover-records
            ["completeBusinessMesChangeover"] = "business.mes.operations.manage", // POST /api/business/v1/mes/changeover-records/{changeoverRecordId}/complete
            ["listBusinessMesShiftHandovers"] = "business.mes.handovers.read", // GET /api/business/v1/mes/shift-handovers
            ["getBusinessMesShiftHandover"] = "business.mes.handovers.read", // GET /api/business/v1/mes/shift-handovers/{handoverId}
            ["createBusinessMesShiftHandover"] = "business.mes.handovers.manage", // POST /api/business/v1/mes/shift-handovers
            ["acceptBusinessMesShiftHandover"] = "business.mes.handovers.manage", // POST /api/business/v1/mes/shift-handovers/{handoverId}/accept
            ["getBusinessMesWorkOrderTraceability"] = "business.mes.traceability.read", // GET /api/business/v1/mes/traceability/work-orders/{workOrderId}
            ["getBusinessMesBatchTraceability"] = "business.mes.traceability.read", // GET /api/business/v1/mes/traceability/batches/{batchOrSerial}
            ["getBusinessMesMaterialLotTraceability"] = "business.mes.traceability.read", // GET /api/business/v1/mes/traceability/material-lots/{materialLotId}
            ["listBusinessMesCapacityImpacts"] = "business.mes.capacity.read", // GET /api/business/v1/mes/capacity-impacts
        };

        var actual = MesEndpointContracts.All.ToDictionary(x => x.OperationId, x => x.PermissionCode, StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (operationId, permissionCode) in expected)
        {
            Assert.True(actual.ContainsKey(operationId), $"端点契约缺少 operationId '{operationId}'。");
            Assert.Equal(permissionCode, actual[operationId]);
        }
    }

    [Fact]
    public void Mes_permission_code_constants_match_their_literal_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CapacityRead"] = "business.mes.capacity.read",
            ["DispatchManage"] = "business.mes.dispatch.manage",
            ["DispatchRead"] = "business.mes.dispatch.read",
            ["DowntimeManage"] = "business.mes.downtime.manage",
            ["DowntimeRead"] = "business.mes.downtime.read",
            ["FoundationRead"] = "business.mes.foundation.read",
            ["HandoversManage"] = "business.mes.handovers.manage",
            ["HandoversRead"] = "business.mes.handovers.read",
            ["MaterialsManage"] = "business.mes.materials.manage",
            ["MaterialsRead"] = "business.mes.materials.read",
            ["OperationsManage"] = "business.mes.operations.manage",
            ["OperationsRead"] = "business.mes.operations.read",
            ["OverviewRead"] = "business.mes.overview.read",
            ["PlansRead"] = "business.mes.plans.read",
            ["QualityRead"] = "business.mes.quality.read",
            ["QualityWrite"] = "business.mes.quality.write",
            ["ReceiptsManage"] = "business.mes.receipts.manage",
            ["ReceiptsRead"] = "business.mes.receipts.read",
            ["ReportingRead"] = "business.mes.reporting.read",
            ["ReportingWrite"] = "business.mes.reporting.write",
            ["SchedulesManage"] = "business.mes.schedules.manage",
            ["SchedulesRead"] = "business.mes.schedules.read",
            ["TraceabilityRead"] = "business.mes.traceability.read",
            ["WorkOrdersManage"] = "business.mes.work-orders.manage",
            ["WorkOrdersRead"] = "business.mes.work-orders.read",
        };

        // GetRawConstantValue 读的是被测程序集的元数据，不是本程序集里被内联的副本，
        // 所以这条断言不依赖测试程序集是否被重新编译。
        var actual = typeof(MesPermissionCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
            .ToDictionary(x => x.Name, x => (string)x.GetRawConstantValue()!, StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, permissionCode) in expected)
        {
            Assert.True(actual.ContainsKey(name), $"MesPermissionCodes 缺少常量 '{name}'。");
            Assert.Equal(permissionCode, actual[name]);
        }
    }
}
