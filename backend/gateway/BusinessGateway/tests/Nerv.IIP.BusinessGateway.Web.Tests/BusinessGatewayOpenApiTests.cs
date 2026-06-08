using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NJsonSchema;
using NJsonSchema.Generation;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using NSwag;
using NSwag.Generation;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayOpenApiTests
{
    [Fact]
    public async Task Business_gateway_exports_openapi_document_with_stable_business_console_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
        });
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        AssertOperationIdsAreUnique(document);

        AssertOperationId(paths, "/api/business-console/v1/master-data/resources", "get", "listBusinessConsoleMasterDataResources");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skus", "get", "listBusinessConsoleSkus");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skus", "post", "createBusinessConsoleSku");
        AssertOperationId(paths, "/api/business-console/v1/master-data/business-partners", "post", "createBusinessConsoleBusinessPartner");
        AssertOperationId(paths, "/api/business-console/v1/master-data/units-of-measure", "post", "createBusinessConsoleUnitOfMeasure");
        AssertOperationId(paths, "/api/business-console/v1/master-data/uom-conversions", "post", "createBusinessConsoleUomConversion");
        AssertOperationId(paths, "/api/business-console/v1/master-data/sites", "post", "createBusinessConsoleSite");
        AssertOperationId(paths, "/api/business-console/v1/master-data/production-lines", "post", "createBusinessConsoleProductionLine");
        AssertOperationId(paths, "/api/business-console/v1/master-data/work-centers", "post", "createBusinessConsoleWorkCenter");
        AssertOperationId(paths, "/api/business-console/v1/master-data/device-assets", "post", "registerBusinessConsoleDeviceAsset");
        AssertOperationId(paths, "/api/business-console/v1/master-data/shifts", "post", "createBusinessConsoleShift");
        AssertOperationId(paths, "/api/business-console/v1/master-data/work-calendars", "post", "createBusinessConsoleWorkCalendar");
        AssertOperationId(paths, "/api/business-console/v1/master-data/teams", "post", "createBusinessConsoleTeam");
        AssertOperationId(paths, "/api/business-console/v1/master-data/departments", "post", "createBusinessConsoleDepartment");
        AssertOperationId(paths, "/api/business-console/v1/master-data/personnel-skills", "post", "assignBusinessConsolePersonnelSkill");
        AssertOperationId(paths, "/api/business-console/v1/master-data/reference-data", "post", "createBusinessConsoleReferenceDataCode");
        AssertOperationId(paths, "/api/business-console/v1/inventory/availability", "get", "getBusinessConsoleInventoryAvailability");
        AssertOperationId(paths, "/api/business-console/v1/inventory/movements", "post", "postBusinessConsoleInventoryMovement");
        AssertOperationId(paths, "/api/business-console/v1/inventory/count-tasks", "post", "createBusinessConsoleInventoryCountTask");
        AssertOperationId(paths, "/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments", "post", "confirmBusinessConsoleInventoryCountAdjustment");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans", "get", "listBusinessConsoleQualityInspectionPlans");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-records", "post", "createBusinessConsoleQualityInspectionRecord");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs", "get", "listBusinessConsoleQualityNcrs");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/disposition", "post", "submitBusinessConsoleQualityNcrDisposition");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/close", "post", "closeBusinessConsoleQualityNcr");
        AssertOperationId(paths, "/api/business-console/v1/engineering/documents", "post", "registerBusinessConsoleEngineeringDocument");
        AssertOperationId(paths, "/api/business-console/v1/engineering/items", "post", "createBusinessConsoleEngineeringItemRevision");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms", "get", "listBusinessConsoleEngineeringBoms");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms/release", "post", "releaseBusinessConsoleEngineeringBom");
        AssertOperationId(paths, "/api/business-console/v1/engineering/manufacturing-boms", "get", "listBusinessConsoleEngineeringManufacturingBoms");
        AssertOperationId(paths, "/api/business-console/v1/engineering/manufacturing-boms/release", "post", "releaseBusinessConsoleEngineeringManufacturingBom");
        AssertOperationId(paths, "/api/business-console/v1/engineering/routings", "get", "listBusinessConsoleEngineeringRoutings");
        AssertOperationId(paths, "/api/business-console/v1/engineering/routings/release", "post", "releaseBusinessConsoleEngineeringRouting");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-changes/release", "post", "releaseBusinessConsoleEngineeringChange");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions", "get", "listBusinessConsoleEngineeringProductionVersions");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions", "post", "createBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions/{productionVersionId}", "put", "updateBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions/{productionVersionId}/archive", "post", "archiveBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions/resolve", "get", "resolveBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/planning/demands", "get", "listBusinessConsolePlanningDemands");
        AssertOperationId(paths, "/api/business-console/v1/planning/demands", "post", "createOrUpdateBusinessConsolePlanningDemand");
        AssertOperationId(paths, "/api/business-console/v1/planning/mrp-runs", "post", "runBusinessConsolePlanningMrp");
        AssertOperationId(paths, "/api/business-console/v1/planning/mrp-runs", "get", "listBusinessConsolePlanningMrpRuns");
        AssertOperationId(paths, "/api/business-console/v1/planning/mrp-runs/{runId}/pegging", "get", "getBusinessConsolePlanningMrpPegging");
        AssertOperationId(paths, "/api/business-console/v1/planning/suggestions", "get", "listBusinessConsolePlanningSuggestions");
        AssertOperationId(paths, "/api/business-console/v1/planning/suggestions/{suggestionId}/accept", "post", "acceptBusinessConsolePlanningSuggestion");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/preview", "post", "previewBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans", "post", "createBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans", "get", "listBusinessConsoleSchedulingPlans");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}", "get", "getBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}/gantt", "get", "getBusinessConsoleSchedulingPlanGantt");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}/release", "post", "releaseBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/equipment/overview", "get", "getBusinessConsoleEquipmentOverview");
        AssertOperationId(paths, "/api/business-console/v1/equipment/devices/{deviceAssetId}", "get", "getBusinessConsoleEquipmentDevice");
        AssertOperationId(paths, "/api/business-console/v1/equipment/availability", "get", "getBusinessConsoleEquipmentAvailability");
        AssertOperationId(paths, "/api/business-console/v1/equipment/alarms", "get", "listBusinessConsoleEquipmentAlarms");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/tags", "get", "listBusinessConsoleTelemetryTags");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarm-rules", "get", "listBusinessConsoleTelemetryAlarmRules");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarm-rules", "post", "createOrUpdateBusinessConsoleTelemetryAlarmRule");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarms", "get", "listBusinessConsoleTelemetryAlarms");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/devices/{deviceAssetId}/history", "get", "queryBusinessConsoleTelemetryDeviceHistory");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/oee", "get", "queryBusinessConsoleTelemetryOee");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/runtime-availability", "get", "queryBusinessConsoleTelemetryRuntimeAvailability");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders", "get", "listBusinessConsoleMaintenanceWorkOrders");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders", "post", "createBusinessConsoleMaintenanceWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders/{workOrderId}", "get", "getBusinessConsoleMaintenanceWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders/{workOrderId}/complete", "post", "completeBusinessConsoleMaintenanceWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/plans", "get", "listBusinessConsoleMaintenancePlans");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/plans", "post", "createBusinessConsoleMaintenancePlan");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/inspections", "post", "recordBusinessConsoleMaintenanceInspection");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/availability-windows", "get", "queryBusinessConsoleMaintenanceAvailabilityWindows");
        AssertOperationId(paths, "/api/business-console/v1/search", "get", "searchBusinessConsoleObjects");
        AssertOperationId(paths, "/api/business-console/v1/workbench/summary", "get", "getBusinessConsoleWorkbenchSummary");
        AssertJsonResponseRef(
            paths,
            "/api/business-console/v1/workbench/summary",
            "get",
            "200",
            "NetCorePalExtensionsDtoResponseDataOfBusinessConsoleWorkbenchSummaryResponse");
        AssertRequiredStringQueryParameter(paths, "/api/business-console/v1/workbench/summary", "get", "organizationId");
        AssertRequiredStringQueryParameter(paths, "/api/business-console/v1/workbench/summary", "get", "environmentId");
        AssertOptionalIntegerQueryParameter(paths, "/api/business-console/v1/workbench/summary", "get", "take");
        AssertJwtBearerSecurity(paths, "/api/business-console/v1/workbench/summary", "get");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-orders", "get", "listBusinessConsoleErpPurchaseOrders");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/rfqs", "get", "listBusinessConsoleErpRequestsForQuotation");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-requisitions/from-suggestion", "post", "createBusinessConsoleErpPurchaseRequisitionFromSuggestion");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/rfqs", "post", "createBusinessConsoleErpRequestForQuotation");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/supplier-quotations", "post", "receiveBusinessConsoleErpSupplierQuotation");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-orders", "post", "createBusinessConsoleErpPurchaseOrder");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-receipts", "post", "recordBusinessConsoleErpPurchaseReceipt");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/sales-orders", "get", "listBusinessConsoleErpSalesOrders");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/opportunities", "get", "listBusinessConsoleErpOpportunities");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/opportunities", "post", "openBusinessConsoleErpOpportunity");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/quotations", "get", "listBusinessConsoleErpQuotations");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/quotations", "post", "createBusinessConsoleErpQuotation");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/quotations/{quotationNo}/approve", "post", "approveBusinessConsoleErpQuotation");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/sales-orders", "post", "createBusinessConsoleErpSalesOrder");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/delivery-orders", "get", "listBusinessConsoleErpDeliveryOrders");
        AssertOperationId(paths, "/api/business-console/v1/erp/sales/delivery-orders", "post", "releaseBusinessConsoleErpDeliveryOrder");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/payables", "post", "createBusinessConsoleErpAccountPayable");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/payables", "get", "listBusinessConsoleErpPayables");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/receivables", "post", "createBusinessConsoleErpAccountReceivable");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/receivables", "get", "listBusinessConsoleErpReceivables");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/cost-candidates", "post", "createBusinessConsoleErpCostCandidate");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/cost-candidates", "get", "listBusinessConsoleErpCostCandidates");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/vouchers", "post", "postBusinessConsoleErpJournalVoucher");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/vouchers", "get", "listBusinessConsoleErpJournalVouchers");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/summary", "get", "getBusinessConsoleErpFinanceSummary");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/payables/by-source", "get", "getBusinessConsoleErpPayableBySourceDocument");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/receivables/by-source", "get", "getBusinessConsoleErpReceivableBySourceDocument");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/cost-candidates/by-source", "get", "getBusinessConsoleErpCostCandidateBySourceDocument");
        foreach (var erpListPath in new[]
        {
            "/api/business-console/v1/erp/procurement/rfqs",
            "/api/business-console/v1/erp/procurement/purchase-orders",
            "/api/business-console/v1/erp/sales/opportunities",
            "/api/business-console/v1/erp/sales/quotations",
            "/api/business-console/v1/erp/sales/sales-orders",
            "/api/business-console/v1/erp/sales/delivery-orders",
            "/api/business-console/v1/erp/finance/payables",
            "/api/business-console/v1/erp/finance/receivables",
            "/api/business-console/v1/erp/finance/cost-candidates",
            "/api/business-console/v1/erp/finance/vouchers",
        })
        {
            AssertQueryParameters(
                paths,
                erpListPath,
                "get",
                "organizationId",
                "environmentId",
                "status",
                "keyword",
                "skip",
                "take");
        }
        AssertOperationId(paths, "/api/business-console/v1/approval/templates", "get", "listBusinessConsoleApprovalTemplates");
        AssertOperationId(paths, "/api/business-console/v1/approval/templates", "post", "createOrUpdateBusinessConsoleApprovalTemplate");
        AssertOperationId(paths, "/api/business-console/v1/approval/chains", "post", "startBusinessConsoleApprovalChain");
        AssertOperationId(paths, "/api/business-console/v1/approval/chains/{chainId}", "get", "getBusinessConsoleApprovalChain");
        AssertOperationId(paths, "/api/business-console/v1/approval/tasks", "get", "listBusinessConsoleApprovalTasks");
        AssertOperationId(paths, "/api/business-console/v1/approval/chains/{chainId}/steps/{stepNo}/resolve", "post", "resolveBusinessConsoleApprovalStep");
        AssertOperationId(paths, "/api/business-console/v1/barcode/rules", "post", "createOrUpdateBusinessConsoleBarcodeRule");
        AssertOperationId(paths, "/api/business-console/v1/barcode/templates", "get", "listBusinessConsoleBarcodeTemplates");
        AssertOperationId(paths, "/api/business-console/v1/barcode/templates", "post", "createOrUpdateBusinessConsoleBarcodeTemplate");
        AssertOperationId(paths, "/api/business-console/v1/barcode/print-batches", "post", "createBusinessConsoleBarcodePrintBatch");
        AssertOperationId(paths, "/api/business-console/v1/barcode/print-batches/{printBatchId}", "get", "getBusinessConsoleBarcodePrintBatch");
        AssertOperationId(paths, "/api/business-console/v1/barcode/scans", "post", "recordBusinessConsoleBarcodeScan");
        AssertOperationId(paths, "/api/business-console/v1/barcode/scans", "get", "listBusinessConsoleBarcodeScans");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders", "get", "listBusinessConsoleWmsInboundOrders");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders", "post", "createBusinessConsoleWmsInboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks", "post", "createBusinessConsoleWmsPutawayTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/complete", "post", "completeBusinessConsoleWmsInboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders", "get", "listBusinessConsoleWmsOutboundOrders");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders", "post", "createBusinessConsoleWmsOutboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", "post", "createBusinessConsoleWmsPickingTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/complete", "post", "completeBusinessConsoleWmsOutboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/count-executions", "post", "createBusinessConsoleWmsCountExecution");
        AssertOperationId(paths, "/api/business-console/v1/wms/count-executions/{countExecutionId}/complete", "post", "completeBusinessConsoleWmsCountExecution");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks", "get", "listBusinessConsoleWmsWcsTasks");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", "post", "dispatchBusinessConsoleWmsWcsTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/fail", "post", "failBusinessConsoleWmsWcsTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/complete", "post", "completeBusinessConsoleWmsWcsTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders", "get", "listBusinessConsoleMesWorkOrders");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness", "get", "getBusinessConsoleMesFoundationReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/master-data", "get", "getBusinessConsoleMesMasterDataReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/product-engineering", "get", "getBusinessConsoleMesProductEngineeringReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/supply", "get", "getBusinessConsoleMesSupplyReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/quality", "get", "getBusinessConsoleMesQualityReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/equipment", "get", "getBusinessConsoleMesEquipmentReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/barcode-numbering", "get", "getBusinessConsoleMesBarcodeNumberingReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/overview", "get", "getBusinessConsoleMesOverview");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-plans", "get", "listBusinessConsoleMesProductionPlans");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-plans/{productionPlanId}/readiness", "get", "getBusinessConsoleMesProductionPlanReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders", "post", "convertBusinessConsoleMesPlanToWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}", "get", "getBusinessConsoleMesWorkOrderDetail");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/release", "post", "releaseBusinessConsoleMesWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/rush", "post", "createBusinessConsoleMesRushWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness", "get", "getBusinessConsoleMesMaterialReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests", "post", "createBusinessConsoleMesMaterialIssueRequest");
        AssertOperationId(paths, "/api/business-console/v1/mes/material-issue-requests", "get", "listBusinessConsoleMesMaterialIssueRequests");
        AssertOperationId(paths, "/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts", "post", "confirmBusinessConsoleMesLineSideMaterialReceipt");
        AssertOperationId(paths, "/api/business-console/v1/mes/dispatch-tasks", "get", "listBusinessConsoleMesDispatchTasks");
        AssertOperationId(paths, "/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign", "post", "assignBusinessConsoleMesDispatchTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks", "get", "listBusinessConsoleMesOperationTasks");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start", "post", "startBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause", "post", "pauseBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume", "post", "resumeBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete", "post", "completeBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/wip", "get", "getBusinessConsoleMesWipSummary");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-reports", "get", "listBusinessConsoleMesProductionReports");
        AssertOperationId(paths, "/api/business-console/v1/mes/schedules/run", "post", "runBusinessConsoleMesSchedule");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-reports", "post", "recordBusinessConsoleMesProductionReport");
        AssertOperationId(paths, "/api/business-console/v1/mes/defects", "post", "recordBusinessConsoleMesDefect");
        AssertOperationId(paths, "/api/business-console/v1/mes/related-quality-items", "get", "listBusinessConsoleMesRelatedQualityItems");
        AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests", "get", "listBusinessConsoleMesFinishedGoodsReceiptRequests");
        AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests", "post", "createBusinessConsoleMesFinishedGoodsReceiptRequest");
        AssertOperationId(paths, "/api/business-console/v1/mes/downtime-events", "get", "listBusinessConsoleMesDowntimeEvents");
        AssertOperationId(paths, "/api/business-console/v1/mes/downtime-events", "post", "recordBusinessConsoleMesDowntimeEvent");
        AssertOperationId(paths, "/api/business-console/v1/mes/downtime-events/{downtimeEventId}/recover", "post", "confirmBusinessConsoleMesDowntimeRecovery");
        AssertOperationId(paths, "/api/business-console/v1/mes/shift-handovers", "get", "listBusinessConsoleMesShiftHandovers");
        AssertOperationId(paths, "/api/business-console/v1/mes/shift-handovers", "post", "createBusinessConsoleMesShiftHandover");
        AssertOperationId(paths, "/api/business-console/v1/mes/shift-handovers/{handoverId}/accept", "post", "acceptBusinessConsoleMesShiftHandover");
        AssertOperationId(paths, "/api/business-console/v1/mes/traceability/work-orders/{workOrderId}", "get", "getBusinessConsoleMesWorkOrderTraceability");
        AssertOperationId(paths, "/api/business-console/v1/mes/traceability/batches/{batchOrSerial}", "get", "getBusinessConsoleMesBatchTraceability");
        AssertOperationId(paths, "/api/business-console/v1/mes/traceability/material-lots/{materialLotId}", "get", "getBusinessConsoleMesMaterialLotTraceability");
        AssertOperationId(paths, "/api/business-console/v1/mes/capacity-impacts", "get", "listBusinessConsoleMesCapacityImpacts");
        AssertOperationId(paths, "/health", "get", "HealthEndpoint");

        foreach (var mesListPath in new[]
        {
            "/api/business-console/v1/mes/work-orders",
            "/api/business-console/v1/mes/production-plans",
            "/api/business-console/v1/mes/material-issue-requests",
            "/api/business-console/v1/mes/dispatch-tasks",
            "/api/business-console/v1/mes/operation-tasks",
            "/api/business-console/v1/mes/wip",
            "/api/business-console/v1/mes/production-reports",
            "/api/business-console/v1/mes/related-quality-items",
            "/api/business-console/v1/mes/finished-goods-receipt-requests",
            "/api/business-console/v1/mes/downtime-events",
            "/api/business-console/v1/mes/shift-handovers",
            "/api/business-console/v1/mes/capacity-impacts",
        })
        {
            AssertQueryParameters(
                paths,
                mesListPath,
                "get",
                "organizationId",
                "environmentId",
                "status",
                "keyword",
                "workCenterId",
                "shiftId",
                "deviceAssetId",
                "skip",
                "take");
        }

        AssertQueryParameters(
            paths,
            "/api/business-console/v1/mes/production-plans",
            "get",
            "organizationId",
            "environmentId",
            "status",
            "keyword",
            "workCenterId",
            "shiftId",
            "deviceAssetId",
            "source",
            "readinessStatus",
            "skip",
            "take");

        AssertQueryParameters(
            paths,
            "/api/business-console/v1/quality/inspection-plans",
            "get",
            "organizationId",
            "environmentId",
            "status",
            "keyword",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/quality/ncrs",
            "get",
            "organizationId",
            "environmentId",
            "status",
            "keyword",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments",
            "post",
            "organizationId",
            "environmentId");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/quality/ncrs/{ncrId}/disposition",
            "post",
            "organizationId",
            "environmentId");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/quality/ncrs/{ncrId}/close",
            "post",
            "organizationId",
            "environmentId");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/scheduling/plans",
            "get",
            "organizationId",
            "environmentId",
            "pageIndex",
            "pageSize");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/scheduling/plans/{planId}",
            "get",
            "organizationId",
            "environmentId");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/scheduling/plans/{planId}/gantt",
            "get",
            "organizationId",
            "environmentId");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/scheduling/plans/{planId}/release",
            "post",
            "organizationId",
            "environmentId");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingSchedulePlanStatusContract", "preview", "generated", "released");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleConflictReasonCodeContract", "dueDate", "capacity", "calendar", "material", "quality", "equipment", "noEligibleResource", "outsideHorizon", "invalidLockedAssignment", "predecessorUnscheduled");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleConflictSeverityContract", "info", "warning", "error");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleChangeTypeContract", "added", "moved", "delayed", "preserved", "blocked");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleSplitPolicyContract", "nonSplittable");
        AssertStringEnumSchema(document, "NervIIPContractsEquipmentRuntimeEquipmentRuntimeSourceType", "device-state", "alarm", "downtime", "maintenance-window", "inspection", "stale-source", "manual-block");
    }

    [Fact]
    public void Scheduling_enum_processor_fails_with_diagnostic_when_expected_schema_is_missing()
    {
        var document = new OpenApiDocument();
        var processor = new SchedulingEnumOpenApiDocumentProcessor();

        var exception = Assert.Throws<InvalidOperationException>(() => processor.Process(CreateDocumentProcessorContext(document)));

        Assert.Contains("Missing Scheduling enum OpenAPI schema", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NervIIPContractsSchedulingSchedulePlanStatusContract", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scheduling_enum_processor_replaces_stale_enum_values_and_names()
    {
        var document = new OpenApiDocument();
        var schema = new JsonSchema { Type = JsonObjectType.Integer };
        schema.Enumeration.Add(0);
        schema.EnumerationNames.Add("Generated");
        document.Components.Schemas["NervIIPContractsSchedulingSchedulePlanStatusContract"] = schema;
        AddSchedulingEnumSchemasExcept(document, "NervIIPContractsSchedulingSchedulePlanStatusContract");
        var processor = new SchedulingEnumOpenApiDocumentProcessor();

        processor.Process(CreateDocumentProcessorContext(document));

        Assert.Equal(JsonObjectType.String, schema.Type);
        Assert.Equal(["preview", "generated", "released"], schema.Enumeration.Select(value => Assert.IsType<string>(value)).ToArray());
        Assert.Empty(schema.EnumerationNames);
    }

    private static void AssertOperationId(JsonElement paths, string path, string method, string operationId)
    {
        Assert.Equal(operationId, paths.GetProperty(path).GetProperty(method).GetProperty("operationId").GetString());
    }

    private static void AssertQueryParameters(JsonElement paths, string path, string method, params string[] names)
    {
        var parameters = paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in names)
        {
            Assert.Contains(name, parameters);
        }
    }

    private static void AssertRequiredStringQueryParameter(JsonElement paths, string path, string method, string name)
    {
        var parameter = FindQueryParameter(paths, path, method, name);

        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal("string", parameter.GetProperty("schema").GetProperty("type").GetString());
    }

    private static void AssertOptionalIntegerQueryParameter(JsonElement paths, string path, string method, string name)
    {
        var parameter = FindQueryParameter(paths, path, method, name);

        Assert.False(parameter.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("integer", parameter.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("int32", parameter.GetProperty("schema").GetProperty("format").GetString());
    }

    private static void AssertJwtBearerSecurity(JsonElement paths, string path, string method)
    {
        var security = paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("security")
            .EnumerateArray();

        Assert.Contains(security, requirement => requirement.TryGetProperty("JWTBearerAuth", out _));
    }

    private static JsonElement FindQueryParameter(JsonElement paths, string path, string method, string name) =>
        paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("in").GetString() == "query"
                && parameter.GetProperty("name").GetString() == name);

    private static void AssertJsonResponseRef(
        JsonElement paths,
        string path,
        string method,
        string statusCode,
        string schemaName)
    {
        var schemaRef = paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        Assert.Equal($"#/components/schemas/{schemaName}", schemaRef);
    }

    private static void AssertStringEnumSchema(JsonDocument document, string schemaName, params string[] values)
    {
        var schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);
        var actualValues = schema.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(values, actualValues);
    }

    private static void AssertOperationIdsAreUnique(JsonDocument document)
    {
        var operations = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value
                .EnumerateObject()
                .Where(operation => IsHttpMethod(operation.Name))
                .Select(operation => (
                    Name: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    OperationId: operation.Value.TryGetProperty("operationId", out var operationId)
                        ? operationId.GetString()
                        : null)))
            .ToArray();

        Assert.Empty(operations.Where(operation => string.IsNullOrWhiteSpace(operation.OperationId)).Select(operation => operation.Name));

        var duplicateOperationIds = operations
            .Where(operation => !string.IsNullOrWhiteSpace(operation.OperationId))
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(operation => operation.Name))}")
            .ToArray();
        Assert.Empty(duplicateOperationIds);
    }

    private static bool IsHttpMethod(string method) =>
        method is "get" or "post" or "put" or "patch" or "delete" or "head" or "options" or "trace";

    private static DocumentProcessorContext CreateDocumentProcessorContext(OpenApiDocument document)
    {
        var settings = new OpenApiDocumentGeneratorSettings();
        var resolver = new JsonSchemaResolver(document, settings.SchemaSettings);
        var generator = new JsonSchemaGenerator(settings.SchemaSettings);
        return new DocumentProcessorContext(document, [], [], resolver, generator, settings);
    }

    private static void AddSchedulingEnumSchemasExcept(OpenApiDocument document, string excludedSchemaName)
    {
        foreach (var schemaName in new[]
        {
            "NervIIPContractsSchedulingSchedulePlanStatusContract",
            "NervIIPContractsSchedulingScheduleConflictReasonCodeContract",
            "NervIIPContractsSchedulingScheduleConflictSeverityContract",
            "NervIIPContractsSchedulingScheduleChangeTypeContract",
            "NervIIPContractsSchedulingScheduleSplitPolicyContract",
            "NervIIPContractsEquipmentRuntimeEquipmentRuntimeSourceType",
        })
        {
            if (!string.Equals(schemaName, excludedSchemaName, StringComparison.Ordinal))
            {
                document.Components.Schemas[schemaName] = new JsonSchema();
            }
        }
    }
}
