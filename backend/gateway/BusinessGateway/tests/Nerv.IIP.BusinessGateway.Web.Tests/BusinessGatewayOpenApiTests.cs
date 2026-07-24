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
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
        });
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        AssertOperationIdsAreUnique(document);

        AssertOperationId(paths, "/api/business-console/v1/master-data/resources", "get", "listBusinessConsoleMasterDataResources");
        AssertOperationId(paths, "/api/business-console/v1/master-data/resources/{resourceType}/{code}", "get", "getBusinessConsoleMasterDataResourceDetail");
        AssertOperationId(paths, "/api/business-console/v1/master-data/resources/{resourceType}/{code}", "patch", "updateBusinessConsoleMasterDataResource");
        AssertOperationId(paths, "/api/business-console/v1/master-data/resources/{resourceType}/{code}/disable", "post", "disableBusinessConsoleMasterDataResource");
        AssertOperationId(paths, "/api/business-console/v1/master-data/resources/{resourceType}/{code}/enable", "post", "enableBusinessConsoleMasterDataResource");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skus", "get", "listBusinessConsoleSkus");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skus", "post", "createBusinessConsoleSku");
        AssertOperationId(paths, "/api/business-console/v1/master-data/product-categories", "get", "listBusinessConsoleProductCategories");
        AssertOperationId(paths, "/api/business-console/v1/master-data/product-categories/{categoryCode}", "get", "getBusinessConsoleProductCategory");
        AssertOperationId(paths, "/api/business-console/v1/master-data/product-categories", "post", "createBusinessConsoleProductCategory");
        AssertOperationId(paths, "/api/business-console/v1/master-data/product-categories/{categoryCode}", "put", "updateBusinessConsoleProductCategory");
        AssertOperationId(paths, "/api/business-console/v1/master-data/product-categories/{categoryCode}/archive", "post", "archiveBusinessConsoleProductCategory");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skills", "get", "listBusinessConsoleSkills");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skills/{skillCode}", "get", "getBusinessConsoleSkill");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skills", "post", "createBusinessConsoleSkill");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skills/{skillCode}", "put", "updateBusinessConsoleSkill");
        AssertOperationId(paths, "/api/business-console/v1/master-data/skills/{skillCode}/archive", "post", "archiveBusinessConsoleSkill");
        AssertOperationId(paths, "/api/business-console/v1/master-data/business-partners", "post", "createBusinessConsoleBusinessPartner");
        AssertBusinessPartnerCreditFields(document);
        AssertOperationId(paths, "/api/business-console/v1/master-data/units-of-measure", "post", "createBusinessConsoleUnitOfMeasure");
        AssertOperationId(paths, "/api/business-console/v1/master-data/uom-conversions", "post", "createBusinessConsoleUomConversion");
        AssertOperationId(paths, "/api/business-console/v1/master-data/workshops", "get", "listBusinessConsoleWorkshops");
        AssertOperationId(paths, "/api/business-console/v1/master-data/workshops", "post", "createBusinessConsoleWorkshop");
        AssertOperationId(paths, "/api/business-console/v1/master-data/workers", "get", "listBusinessConsoleWorkers");
        AssertOperationId(paths, "/api/business-console/v1/master-data/teams/{teamCode}/members", "get", "listBusinessConsoleTeamMembers");
        AssertOperationId(paths, "/api/business-console/v1/master-data/teams/{teamCode}/members", "post", "addBusinessConsoleTeamMember");
        AssertOperationId(paths, "/api/business-console/v1/master-data/teams/{teamCode}/members/{userId}", "delete", "removeBusinessConsoleTeamMember");
        AssertOperationId(paths, "/api/business-console/v1/master-data/sites", "post", "createBusinessConsoleSite");
        AssertOperationId(paths, "/api/business-console/v1/master-data/production-lines", "post", "createBusinessConsoleProductionLine");
        AssertOperationId(paths, "/api/business-console/v1/master-data/work-centers", "post", "createBusinessConsoleWorkCenter");
        AssertOperationId(paths, "/api/business-console/v1/master-data/device-assets", "get", "listBusinessConsoleDeviceAssets");
        AssertOperationId(paths, "/api/business-console/v1/master-data/device-assets", "post", "registerBusinessConsoleDeviceAsset");
        AssertOperationId(paths, "/api/business-console/v1/master-data/shifts", "post", "createBusinessConsoleShift");
        AssertOperationId(paths, "/api/business-console/v1/master-data/work-calendars", "post", "createBusinessConsoleWorkCalendar");
        AssertOperationId(paths, "/api/business-console/v1/master-data/teams", "post", "createBusinessConsoleTeam");
        AssertOperationId(paths, "/api/business-console/v1/master-data/departments", "post", "createBusinessConsoleDepartment");
        AssertOperationId(paths, "/api/business-console/v1/master-data/personnel-skills", "post", "assignBusinessConsolePersonnelSkill");
        AssertOperationId(paths, "/api/business-console/v1/master-data/personnel-skills/matrix", "get", "listBusinessConsolePersonnelSkillMatrix");
        AssertOperationId(paths, "/api/business-console/v1/master-data/reference-data", "post", "createBusinessConsoleReferenceDataCode");
        AssertOperationId(paths, "/api/business-console/v1/master-data/code-rules", "get", "listBusinessConsoleCodeRules");
        AssertOperationId(paths, "/api/business-console/v1/master-data/code-rules/{ruleKey}", "get", "getBusinessConsoleCodeRule");
        AssertOperationId(paths, "/api/business-console/v1/master-data/code-rules/{ruleKey}/versions", "post", "createBusinessConsoleCodeRuleVersion");
        AssertOperationId(paths, "/api/business-console/v1/master-data/code-rules/{ruleKey}/preview", "post", "previewBusinessConsoleCodeRule");
        AssertOperationId(paths, "/api/business-console/v1/inventory/availability", "get", "getBusinessConsoleInventoryAvailability");
        AssertOperationId(paths, "/api/business-console/v1/inventory/expiry-alerts", "get", "listBusinessConsoleInventoryExpiryAlerts");
        AssertOperationId(paths, "/api/business-console/v1/inventory/movements", "post", "postBusinessConsoleInventoryMovement");
        AssertOperationId(paths, "/api/business-console/v1/inventory/count-tasks", "post", "createBusinessConsoleInventoryCountTask");
        AssertOperationId(paths, "/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments", "post", "confirmBusinessConsoleInventoryCountAdjustment");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans", "get", "listBusinessConsoleQualityInspectionPlans");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans", "post", "createBusinessConsoleQualityInspectionPlan");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans/{inspectionPlanId}/activate", "post", "activateBusinessConsoleQualityInspectionPlan");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-records", "get", "listBusinessConsoleQualityInspectionRecords");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-records", "post", "createBusinessConsoleQualityInspectionRecord");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr", "post", "openBusinessConsoleQualityNcrFromInspection");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-tasks", "get", "listBusinessConsoleQualityInspectionTasks");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record", "post", "createBusinessConsoleQualityInspectionRecordFromTask");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs", "get", "listBusinessConsoleQualityNcrs");
        AssertOperationId(paths, "/api/business-console/v1/quality/reason-codes", "get", "listBusinessConsoleQualityReasonCodes");
        AssertOperationId(paths, "/api/business-console/v1/quality/reason-codes/{reasonCode}", "get", "getBusinessConsoleQualityReasonCode");
        AssertOperationId(paths, "/api/business-console/v1/quality/reason-codes", "post", "createBusinessConsoleQualityReasonCode");
        AssertOperationId(paths, "/api/business-console/v1/quality/reason-codes/{reasonCode}", "put", "updateBusinessConsoleQualityReasonCode");
        AssertOperationId(paths, "/api/business-console/v1/quality/reason-codes/{reasonCode}/archive", "post", "archiveBusinessConsoleQualityReasonCode");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/disposition", "post", "submitBusinessConsoleQualityNcrDisposition");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/close", "post", "closeBusinessConsoleQualityNcr");
        AssertRequiredStringBodyProperty(document, paths, "/api/business-console/v1/quality/ncrs/{ncrId}/close", "post", "reason", 500);
        AssertOperationId(paths, "/api/business-console/v1/engineering/documents", "post", "registerBusinessConsoleEngineeringDocument");
        AssertOperationId(paths, "/api/business-console/v1/engineering/sops/publish", "post", "publishBusinessConsoleEngineeringSopDocument");
        AssertOperationId(paths, "/api/business-console/v1/engineering/sops/current", "get", "getBusinessConsoleCurrentEngineeringSopDocuments");
        AssertOperationId(paths, "/api/business-console/v1/files/{fileId}/download-grants", "post", "createBusinessConsoleSopFileDownloadGrant");
        AssertOperationId(paths, "/api/business-console/v1/files/download-grants/{downloadGrantId}/content", "get", "downloadBusinessConsoleSopFileContent");
        AssertOperationId(paths, "/api/business-console/v1/engineering/items", "post", "createBusinessConsoleEngineeringItemRevision");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms", "get", "listBusinessConsoleEngineeringBoms");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms/explosion", "get", "getBusinessConsoleEngineeringBomExplosion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms/where-used", "get", "getBusinessConsoleEngineeringBomWhereUsed");
        AssertOperationId(paths, "/api/business-console/v1/engineering/boms/diff", "get", "getBusinessConsoleEngineeringBomDiff");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms/release", "post", "releaseBusinessConsoleEngineeringBom");
        AssertOperationId(paths, "/api/business-console/v1/engineering/manufacturing-boms", "get", "listBusinessConsoleEngineeringManufacturingBoms");
        AssertOperationId(paths, "/api/business-console/v1/engineering/manufacturing-boms/explosion", "get", "getBusinessConsoleEngineeringManufacturingBomExplosion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/manufacturing-boms/where-used", "get", "getBusinessConsoleEngineeringManufacturingBomWhereUsed");
        AssertOperationId(paths, "/api/business-console/v1/engineering/manufacturing-boms/release", "post", "releaseBusinessConsoleEngineeringManufacturingBom");
        AssertOperationId(paths, "/api/business-console/v1/engineering/routings", "get", "listBusinessConsoleEngineeringRoutings");
        AssertOperationId(paths, "/api/business-console/v1/engineering/routings/release", "post", "releaseBusinessConsoleEngineeringRouting");
        AssertOperationId(paths, "/api/business-console/v1/engineering/standard-operations", "get", "listBusinessConsoleEngineeringStandardOperations");
        AssertOperationId(paths, "/api/business-console/v1/engineering/standard-operations/{operationCode}", "get", "getBusinessConsoleEngineeringStandardOperation");
        AssertOperationId(paths, "/api/business-console/v1/engineering/standard-operations", "post", "createBusinessConsoleEngineeringStandardOperation");
        AssertOperationId(paths, "/api/business-console/v1/engineering/standard-operations/{operationCode}", "put", "updateBusinessConsoleEngineeringStandardOperation");
        AssertOperationId(paths, "/api/business-console/v1/engineering/standard-operations/{operationCode}/archive", "post", "archiveBusinessConsoleEngineeringStandardOperation");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-changes/release", "post", "releaseBusinessConsoleEngineeringChange");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-changes/cancel-scheduled", "post", "cancelScheduledBusinessConsoleEngineeringChange");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-changes/reschedule", "post", "rescheduleBusinessConsoleEngineeringChange");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-changes/impact-preview", "post", "previewBusinessConsoleEngineeringChangeImpact");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions", "get", "listBusinessConsoleEngineeringProductionVersions");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions", "post", "createBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions/{productionVersionId}", "put", "updateBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions/{productionVersionId}/archive", "post", "archiveBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions/resolve", "get", "resolveBusinessConsoleEngineeringProductionVersion");
        AssertOperationId(paths, "/api/business-console/v1/planning/mps", "get", "listBusinessConsolePlanningMpsBuckets");
        AssertOperationId(paths, "/api/business-console/v1/planning/mps", "post", "createBusinessConsolePlanningMpsBucket");
        AssertOperationId(paths, "/api/business-console/v1/planning/mps/{mpsId}", "put", "updateBusinessConsolePlanningMpsBucket");
        AssertOperationId(paths, "/api/business-console/v1/planning/mps/{mpsId}/review", "post", "reviewBusinessConsolePlanningMpsBucket");
        AssertOperationId(paths, "/api/business-console/v1/planning/mps/{mpsId}/release", "post", "releaseBusinessConsolePlanningMpsBucket");
        AssertOperationId(paths, "/api/business-console/v1/planning/demands", "get", "listBusinessConsolePlanningDemands");
        AssertOperationId(paths, "/api/business-console/v1/planning/demands", "post", "createOrUpdateBusinessConsolePlanningDemand");
        AssertOperationId(paths, "/api/business-console/v1/planning/demands/{demandSourceId}/cancel", "post", "cancelBusinessConsolePlanningDemand");
        AssertOperationId(paths, "/api/business-console/v1/planning/forecasts", "get", "listBusinessConsolePlanningForecasts");
        AssertOperationId(paths, "/api/business-console/v1/planning/forecasts", "post", "createOrUpdateBusinessConsolePlanningForecast");
        AssertOperationId(paths, "/api/business-console/v1/planning/mrp-runs", "post", "runBusinessConsolePlanningMrp");
        AssertOperationId(paths, "/api/business-console/v1/planning/mrp-runs", "get", "listBusinessConsolePlanningMrpRuns");
        AssertOperationId(paths, "/api/business-console/v1/planning/mrp-runs/{runId}/pegging", "get", "getBusinessConsolePlanningMrpPegging");
        AssertOperationId(paths, "/api/business-console/v1/planning/suggestions", "get", "listBusinessConsolePlanningSuggestions");
        AssertOperationId(paths, "/api/business-console/v1/planning/suggestions/{suggestionId}/accept", "post", "acceptBusinessConsolePlanningSuggestion");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/preview", "post", "previewBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans", "post", "createBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/workbench/plans", "post", "createBusinessConsoleSchedulingWorkbenchPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}/revisions", "post", "createBusinessConsoleSchedulingPlanRevision");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans", "get", "listBusinessConsoleSchedulingPlans");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}", "get", "getBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}/gantt", "get", "getBusinessConsoleSchedulingPlanGantt");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}/release", "post", "releaseBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/plans/{planId}/revoke", "post", "revokeBusinessConsoleSchedulingPlan");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/order-urgencies", "get", "listBusinessConsoleOrderUrgencies");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/order-urgencies/{orderReference}", "get", "getBusinessConsoleOrderUrgency");
        AssertOperationId(paths, "/api/business-console/v1/scheduling/order-urgencies/{orderReference}/business-priority", "put", "setBusinessConsoleOrderUrgencyBusinessPriority");
        AssertOperationId(paths, "/api/business-console/v1/equipment/overview", "get", "getBusinessConsoleEquipmentOverview");
        AssertOperationId(paths, "/api/business-console/v1/equipment/devices/{deviceAssetId}", "get", "getBusinessConsoleEquipmentDevice");
        AssertOperationId(paths, "/api/business-console/v1/equipment/availability", "get", "getBusinessConsoleEquipmentAvailability");
        AssertOperationId(paths, "/api/business-console/v1/equipment/alarms", "get", "listBusinessConsoleEquipmentAlarms");
        AssertOperationId(paths, "/api/business-console/v1/equipment/alarms/{alarmEventId}/acknowledge", "post", "acknowledgeBusinessConsoleEquipmentAlarm");
        AssertOperationId(paths, "/api/business-console/v1/equipment/alarms/{alarmEventId}/shelve", "post", "shelveBusinessConsoleEquipmentAlarm");
        AssertOperationId(paths, "/api/business-console/v1/equipment/alarms/{alarmEventId}/unshelve", "post", "unshelveBusinessConsoleEquipmentAlarm");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/tags", "get", "listBusinessConsoleTelemetryTags");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/connectors/{connectorId}/collection-health", "get", "queryBusinessConsoleTelemetryConnectorCollectionHealth");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/connectors/collection-health", "get", "listBusinessConsoleTelemetryConnectorCollectionHealth");
        AssertConnectorCollectionHealthFields(document);
        AssertOperationId(paths, "/api/business-console/v1/telemetry/connectors/{connectorId}/tag-coverage", "get", "getBusinessConsoleTelemetryConnectorTagCoverage");
        AssertConnectorTagCoverageFields(document);
        AssertOperationId(paths, "/api/business-console/v1/telemetry/tags/current-value", "get", "getBusinessConsoleTelemetryTagCurrentValue");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarm-rules", "get", "listBusinessConsoleTelemetryAlarmRules");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarm-rules", "post", "createOrUpdateBusinessConsoleTelemetryAlarmRule");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/device-control-commands", "post", "createBusinessConsoleTelemetryDeviceControlCommand");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/device-control-commands", "get", "listBusinessConsoleTelemetryDeviceControlCommands");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/device-control-commands/{commandId}", "get", "getBusinessConsoleTelemetryDeviceControlCommand");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/device-control-bindings", "get", "listBusinessConsoleTelemetryDeviceControlBindings");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/device-control-bindings", "post", "createOrUpdateBusinessConsoleTelemetryDeviceControlBinding");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/device-control-bindings/{deviceAssetId}/disable", "post", "disableBusinessConsoleTelemetryDeviceControlBinding");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/samples", "post", "recordBusinessConsoleTelemetrySample");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarms", "post", "postBusinessConsoleTelemetryAlarm");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/alarms", "get", "listBusinessConsoleTelemetryAlarms");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/devices/{deviceAssetId}/history", "get", "queryBusinessConsoleTelemetryDeviceHistory");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/oee", "get", "queryBusinessConsoleTelemetryOee");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/runtime-availability", "get", "queryBusinessConsoleTelemetryRuntimeAvailability");
        AssertOperationId(paths, "/api/business-console/v1/telemetry/runtime-hours", "get", "queryBusinessConsoleTelemetryRuntimeHours");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders", "get", "listBusinessConsoleMaintenanceWorkOrders");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders", "post", "createBusinessConsoleMaintenanceWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders/{workOrderId}", "get", "getBusinessConsoleMaintenanceWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/work-orders/{workOrderId}/complete", "post", "completeBusinessConsoleMaintenanceWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/plans", "get", "listBusinessConsoleMaintenancePlans");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/plans", "post", "createBusinessConsoleMaintenancePlan");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/plans/{planId}", "put", "updateBusinessConsoleMaintenancePlan");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/inspections", "post", "recordBusinessConsoleMaintenanceInspection");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/inspections", "get", "listBusinessConsoleMaintenanceInspections");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/spare-parts", "get", "listBusinessConsoleMaintenanceSpareParts");
        AssertOperationId(paths, "/api/business-console/v1/maintenance/spare-parts", "post", "createBusinessConsoleMaintenanceSparePart");
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
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-requisitions", "get", "listBusinessConsoleErpPurchaseRequisitions");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-requisitions/from-suggestion", "post", "createBusinessConsoleErpPurchaseRequisitionFromSuggestion");
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-requisitions/convert-to-purchase-order", "post", "convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrder");
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
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/work-center-cost-rates", "post", "configureBusinessConsoleErpWorkCenterCostRate");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/work-center-cost-rates", "get", "listBusinessConsoleErpWorkCenterCostRates");
        AssertRequiredBodyProperty(
            document,
            paths,
            "/api/business-console/v1/erp/finance/work-center-cost-rates",
            "post",
            "effectiveFromUtc");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/vouchers", "post", "postBusinessConsoleErpJournalVoucher");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/payment-executions", "post", "approveBusinessConsoleErpPaymentExecution");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/payment-executions/{paymentExecutionNo}/execute", "post", "executeBusinessConsoleErpPaymentExecution");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/cash-receipts", "post", "registerBusinessConsoleErpCashReceipt");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/cash-receipts/{cashReceiptNo}/match", "post", "matchBusinessConsoleErpCashReceipt");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/accounting-periods", "post", "openBusinessConsoleErpAccountingPeriod");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/accounting-periods/close", "post", "closeBusinessConsoleErpAccountingPeriod");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/accounting-periods/reopen", "post", "reopenBusinessConsoleErpAccountingPeriod");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/vouchers", "get", "listBusinessConsoleErpJournalVouchers");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/trial-balance", "get", "getBusinessConsoleErpTrialBalance");
        AssertOperationId(paths, "/api/business-console/v1/erp/finance/month-end-checklist", "get", "getBusinessConsoleErpMonthEndChecklist");
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
        AssertOperationId(paths, "/api/business-console/v1/approval/chains", "get", "listBusinessConsoleApprovalChains");
        AssertOperationId(paths, "/api/business-console/v1/approval/chains", "post", "startBusinessConsoleApprovalChain");
        AssertOperationId(paths, "/api/business-console/v1/approval/chains/{chainId}", "get", "getBusinessConsoleApprovalChain");
        AssertOperationId(paths, "/api/business-console/v1/approval/tasks", "get", "listBusinessConsoleApprovalTasks");
        AssertOperationId(paths, "/api/business-console/v1/approval/decisions", "get", "listBusinessConsoleApprovalDecisions");
        AssertOperationId(paths, "/api/business-console/v1/approval/chains/{chainId}/steps/{stepNo}/resolve", "post", "resolveBusinessConsoleApprovalStep");
        AssertOperationId(paths, "/api/business-console/v1/approval/delegations", "get", "listBusinessConsoleApprovalDelegations");
        AssertOperationId(paths, "/api/business-console/v1/approval/delegations", "post", "createBusinessConsoleApprovalDelegation");
        AssertOperationId(paths, "/api/business-console/v1/approval/delegations/{delegationId}/revoke", "post", "revokeBusinessConsoleApprovalDelegation");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/approval/templates",
            "get",
            "organizationId",
            "environmentId",
            "documentType",
            "isActive",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/approval/tasks",
            "get",
            "organizationId",
            "environmentId",
            "actorType",
            "actorRef",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/approval/chains",
            "get",
            "organizationId",
            "environmentId",
            "status",
            "startedBy",
            "sourceService",
            "documentType",
            "documentId",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/approval/decisions",
            "get",
            "organizationId",
            "environmentId",
            "chainId",
            "actorType",
            "actorRef",
            "decision",
            "documentType",
            "documentId",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/approval/delegations",
            "get",
            "organizationId",
            "environmentId",
            "status",
            "delegatorActorRef",
            "delegateActorRef",
            "documentType",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/approval/delegations/{delegationId}/revoke",
            "post",
            "organizationId",
            "environmentId");
        AssertOperationId(paths, "/api/business-console/v1/barcode/rules", "get", "listBusinessConsoleBarcodeRules");
        AssertOperationId(paths, "/api/business-console/v1/barcode/rules", "post", "createOrUpdateBusinessConsoleBarcodeRule");
        AssertOperationId(paths, "/api/business-console/v1/barcode/templates", "get", "listBusinessConsoleBarcodeTemplates");
        AssertOperationId(paths, "/api/business-console/v1/barcode/templates", "post", "createOrUpdateBusinessConsoleBarcodeTemplate");
        AssertOperationId(paths, "/api/business-console/v1/barcode/print-batches", "post", "createBusinessConsoleBarcodePrintBatch");
        AssertOperationId(paths, "/api/business-console/v1/barcode/print-batches", "get", "listBusinessConsoleBarcodePrintBatches");
        AssertOperationId(paths, "/api/business-console/v1/barcode/print-batches/{printBatchId}", "get", "getBusinessConsoleBarcodePrintBatch");
        AssertOperationId(paths, "/api/business-console/v1/barcode/scans", "post", "recordBusinessConsoleBarcodeScan");
        AssertOperationId(paths, "/api/business-console/v1/barcode/scans", "get", "listBusinessConsoleBarcodeScans");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders", "get", "listBusinessConsoleWmsInboundOrders");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders", "post", "createBusinessConsoleWmsInboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks", "post", "createBusinessConsoleWmsPutawayTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/putaway-tasks", "get", "listBusinessConsoleWmsPutawayTasks");
        AssertQueryParameterDescription(
            paths,
            "/api/business-console/v1/wms/putaway-tasks",
            "get",
            "operatorUserId",
            WmsWarehouseTaskOpenApiDocumentProcessor.OperatorUserIdDescription);
        AssertOperationId(paths, "/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/complete", "post", "completeBusinessConsoleWmsInboundOrder");
        AssertRequiredSchemaProperty(document, "BusinessConsoleWmsInboundLineCaptureInput", "lineNo");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders", "get", "listBusinessConsoleWmsOutboundOrders");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders", "post", "createBusinessConsoleWmsOutboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", "post", "createBusinessConsoleWmsPickingTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/picking-tasks", "get", "listBusinessConsoleWmsPickingTasks");
        AssertQueryParameterDescription(
            paths,
            "/api/business-console/v1/wms/picking-tasks",
            "get",
            "operatorUserId",
            WmsWarehouseTaskOpenApiDocumentProcessor.OperatorUserIdDescription);
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/complete", "post", "completeBusinessConsoleWmsOutboundOrder");
        AssertOperationId(paths, "/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry", "post", "retryBusinessConsoleWmsOutboundInventoryPosting");
        AssertOperationId(paths, "/api/business-console/v1/wms/count-executions", "post", "createBusinessConsoleWmsCountExecution");
        AssertOperationId(paths, "/api/business-console/v1/wms/count-executions", "get", "listBusinessConsoleWmsCountExecutions");
        AssertOperationId(paths, "/api/business-console/v1/wms/count-executions/{countExecutionId}/complete", "post", "completeBusinessConsoleWmsCountExecution");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks", "get", "listBusinessConsoleWmsWcsTasks");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", "post", "dispatchBusinessConsoleWmsWcsTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/fail", "post", "failBusinessConsoleWmsWcsTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/complete", "post", "completeBusinessConsoleWmsWcsTask");
        AssertOperationId(paths, "/api/business-console/v1/wms/receiving-quality-gates", "get", "listBusinessConsoleWmsReceivingQualityGates");
        AssertOperationId(paths, "/api/business-console/v1/wms/supplier-return-requests", "get", "listBusinessConsoleWmsSupplierReturnRequests");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders", "get", "listBusinessConsoleMesWorkOrders");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness", "get", "getBusinessConsoleMesFoundationReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/master-data", "get", "getBusinessConsoleMesMasterDataReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/product-engineering", "get", "getBusinessConsoleMesProductEngineeringReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/supply", "get", "getBusinessConsoleMesSupplyReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/quality", "get", "getBusinessConsoleMesQualityReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/equipment", "get", "getBusinessConsoleMesEquipmentReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/foundation-readiness/barcode-coding", "get", "getBusinessConsoleMesBarcodeCodingReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/overview", "get", "getBusinessConsoleMesOverview");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-plans", "get", "listBusinessConsoleMesProductionPlans");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-plans/{productionPlanId}/readiness", "get", "getBusinessConsoleMesProductionPlanReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders", "post", "convertBusinessConsoleMesPlanToWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}", "get", "getBusinessConsoleMesWorkOrderDetail");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/release", "post", "releaseBusinessConsoleMesWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/hold", "post", "holdBusinessConsoleMesWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/cancel", "post", "cancelBusinessConsoleMesWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/quality-holds/{sourceDocumentId}/force-release", "post", "forceReleaseBusinessConsoleMesQualityHold");
        AssertOperationId(paths, "/api/business-console/v1/mes/quality-holds/{sourceDocumentId}/timeline", "get", "getBusinessConsoleMesQualityHoldTimeline");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-reports/{reportNo}/reverse", "post", "reverseBusinessConsoleMesProductionReport");
        AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-posting/retry", "post", "retryBusinessConsoleMesFinishedGoodsReceiptInventoryPosting");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/rush", "post", "createBusinessConsoleMesRushWorkOrder");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness", "get", "getBusinessConsoleMesMaterialReadiness");
        AssertOperationId(paths, "/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests", "post", "createBusinessConsoleMesMaterialIssueRequest");
        AssertOperationId(paths, "/api/business-console/v1/mes/material-issue-requests", "get", "listBusinessConsoleMesMaterialIssueRequests");
        AssertOperationId(paths, "/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts", "post", "confirmBusinessConsoleMesLineSideMaterialReceipt");
        AssertOperationId(paths, "/api/business-console/v1/mes/dispatch-tasks", "get", "listBusinessConsoleMesDispatchTasks");
        AssertOperationId(paths, "/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign", "post", "assignBusinessConsoleMesDispatchTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks", "get", "listBusinessConsoleMesOperationTasks");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-sops/current", "get", "getBusinessConsoleMesCurrentOperationSops");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start", "post", "startBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause", "post", "pauseBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume", "post", "resumeBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete", "post", "completeBusinessConsoleMesOperationTask");
        AssertOperationId(paths, "/api/business-console/v1/mes/wip", "get", "getBusinessConsoleMesWipSummary");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-reports", "get", "listBusinessConsoleMesProductionReports");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-reports/{reportNo}", "get", "getBusinessConsoleMesProductionReport");
        AssertOperationId(paths, "/api/business-console/v1/mes/schedules/run", "post", "runBusinessConsoleMesSchedule");
        AssertOperationId(paths, "/api/business-console/v1/mes/production-reports", "post", "recordBusinessConsoleMesProductionReport");
        AssertOperationId(paths, "/api/business-console/v1/mes/defects", "post", "recordBusinessConsoleMesDefect");
        AssertOperationId(paths, "/api/business-console/v1/mes/related-quality-items", "get", "listBusinessConsoleMesRelatedQualityItems");
        AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests", "get", "listBusinessConsoleMesFinishedGoodsReceiptRequests");
        AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests", "post", "createBusinessConsoleMesFinishedGoodsReceiptRequest");
        AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-link", "get", "getBusinessConsoleMesFinishedGoodsReceiptInventoryLink");
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
            AssertMesStatusQueryEnum(paths, mesListPath);
        }

        AssertQueryParameters(
            paths,
            "/api/business-console/v1/mes/production-reports",
            "get",
            "organizationId",
            "environmentId",
            "keyword",
            "workCenterId",
            "shiftId",
            "deviceAssetId",
            "skip",
            "take");
        AssertNoQueryParameter(paths, "/api/business-console/v1/mes/production-reports", "get", "status");

        AssertMesListDisplayContract(document);

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
            "/api/business-console/v1/quality/inspection-records",
            "get",
            "organizationId",
            "environmentId",
            "status",
            "keyword",
            "skip",
            "take");
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr",
            "post",
            "organizationId",
            "environmentId");
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
        AssertQueryParameters(
            paths,
            "/api/business-console/v1/scheduling/plans/{planId}/revoke",
            "post",
            "organizationId",
            "environmentId");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingSchedulePlanStatusContract", "preview", "generated", "released", "superseded", "revoked");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleConflictReasonCodeContract", "dueDate", "capacity", "calendar", "material", "quality", "equipment", "noEligibleResource", "outsideHorizon", "invalidLockedAssignment", "predecessorUnscheduled", "tooling");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleConflictSeverityContract", "info", "warning", "error");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleChangeTypeContract", "added", "moved", "delayed", "preserved", "blocked");
        AssertStringEnumSchema(document, "NervIIPContractsSchedulingScheduleSplitPolicyContract", "nonSplittable");
        AssertStringEnumSchema(document, "NervIIPContractsEquipmentRuntimeEquipmentRuntimeSourceType", "device-state", "alarm", "downtime", "maintenance-window", "inspection", "stale-source", "manual-block");
    }

    private static void AssertRequiredStringBodyProperty(
        JsonDocument document,
        JsonElement paths,
        string path,
        string method,
        string propertyName,
        int maxLength)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        var schemaRef = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var schemaName = schemaRef.Split('/')[^1];
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        Assert.Contains(propertyName, schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(maxLength, schema.GetProperty("properties").GetProperty(propertyName).GetProperty("maxLength").GetInt32());
    }

    private static void AssertRequiredBodyProperty(
        JsonDocument document,
        JsonElement paths,
        string path,
        string method,
        string propertyName)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        var schemaRef = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var schemaName = schemaRef.Split('/')[^1];
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        Assert.Contains(propertyName, schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
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
        Assert.Equal(
            ["preview", "generated", "released", "superseded", "revoked"],
            schema.Enumeration.Select(value => Assert.IsType<string>(value)).ToArray());
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

    private static void AssertQueryParameterDescription(JsonElement paths, string path, string method, string name, string description)
    {
        var parameter = FindQueryParameter(paths, path, method, name);

        Assert.Equal(description, parameter.GetProperty("description").GetString());
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

    private static void AssertNoQueryParameter(JsonElement paths, string path, string method, string name)
    {
        var parameters = paths.GetProperty(path)
            .GetProperty(method)
            .GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .Select(parameter => parameter.GetProperty("name").GetString());

        Assert.DoesNotContain(name, parameters);
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

    private static void AssertBusinessPartnerCreditFields(JsonDocument document)
    {
        var properties = FindSchemaBySuffix(document, "BusinessConsoleCreateBusinessPartnerRequest")
            .GetProperty("properties");

        Assert.True(properties.TryGetProperty("creditLimit", out var creditLimit), "Business partner create request must expose creditLimit.");
        Assert.Equal("number", creditLimit.GetProperty("type").GetString());
        Assert.True(properties.TryGetProperty("creditCurrencyCode", out var creditCurrencyCode), "Business partner create request must expose creditCurrencyCode.");
        Assert.Equal("string", creditCurrencyCode.GetProperty("type").GetString());
    }

    private static void AssertConnectorCollectionHealthFields(JsonDocument document)
    {
        AssertSchemaProperties(
            document,
            "BusinessConsoleConnectorCollectionHealthResponse",
            "connection",
            "staleReason",
            "offlineReason",
            "hostLivenessDeadlineUtc");
        AssertSchemaProperties(
            document,
            "BusinessConsoleConnectorCollectionHealthListItem",
            "connection",
            "staleReason",
            "offlineReason",
            "hostLivenessDeadlineUtc");
        AssertSchemaProperties(
            document,
            "BusinessConsoleConnectorConnectionState",
            "status",
            "observedAtUtc",
            "connectedSinceUtc",
            "disconnectedSinceUtc",
            "reasonCategory",
            "diagnosticCode");
    }

    private static void AssertConnectorTagCoverageFields(JsonDocument document)
    {
        AssertSchemaProperties(
            document,
            "BusinessConsoleConnectorTagCoverageResponse",
            "collectionConnectorId",
            "manifestStatus",
            "manifestRevision",
            "manifestObservedAtUtc",
            "configuredCount",
            "enabledCount",
            "activeCount",
            "everSampledCount",
            "errorCount",
            "items");
        AssertSchemaProperties(
            document,
            "BusinessConsoleConnectorTagCoverageItem",
            "deviceAssetId",
            "tagKey",
            "enabled",
            "activationStatus",
            "activationObservedAtUtc",
            "activationErrorCode",
            "activationErrorMessage",
            "firstSampleAtUtc",
            "lastSampleAtUtc");
    }

    private static void AssertSchemaProperties(JsonDocument document, string schemaNameSuffix, params string[] propertyNames)
    {
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(schema =>
                schema.Name.EndsWith(schemaNameSuffix, StringComparison.Ordinal)
                && schema.Value.TryGetProperty("properties", out _))
            .ToArray();
        var schema = Assert.Single(schemas).Value;
        var properties = schema.GetProperty("properties");
        foreach (var propertyName in propertyNames)
        {
            Assert.True(
                properties.TryGetProperty(propertyName, out _),
                $"{schemaNameSuffix} must expose {propertyName}.");
        }
    }

    private static void AssertMesListDisplayContract(JsonDocument document)
    {
        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesCapacityImpactRow",
            "workCenterCode",
            "workCenterName",
            "deviceAssetCode",
            "deviceAssetName");
        AssertMesStatusEnum(document, "BusinessConsoleMesCapacityImpactRow", "status");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesDowntimeEventRow",
            "workOrderNo",
            "operationTaskNo",
            "deviceAssetCode",
            "deviceAssetName");
        AssertMesStatusEnum(document, "BusinessConsoleMesDowntimeEventRow", "status");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesOperationTaskRow",
            "workOrderNo",
            "operationTaskNo",
            "workCenterCode",
            "workCenterName",
            "deviceAssetCode",
            "deviceAssetName");
        AssertMesStatusEnum(document, "BusinessConsoleMesOperationTaskRow", "status");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesDispatchTaskRow",
            "workOrderNo",
            "operationTaskNo",
            "workCenterCode",
            "workCenterName",
            "deviceAssetCode",
            "deviceAssetName");
        AssertMesStatusEnum(document, "BusinessConsoleMesDispatchTaskRow", "status");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesWipSummaryRow",
            "workOrderNo",
            "operationTaskNo",
            "workCenterCode",
            "workCenterName");
        AssertMesStatusEnum(document, "BusinessConsoleMesWipSummaryRow", "status");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesMaterialIssueRequestRow",
            "workOrderNo",
            "operationTaskNo",
            "materialCode");
        AssertMesStatusEnum(document, "BusinessConsoleMesMaterialIssueRequestRow", "status");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesProductionReportRow",
            "workOrderNo",
            "operationTaskNo",
            // MAN-444/#798: 冲销互链与工单状态字段,支撑 Console 负向记录标记、原单⇄冲销单双向高亮与冲销按钮分级。
            "reversedReportNo",
            "reversalReason",
            "workOrderStatus",
            // 服务端逐行反查的已冲销互链,支撑跨分页稳定的"已冲销"判定与原单→冲销单互链(review 修复)。
            "reversalReportNo");

        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesReceiptRequestRow",
            "workOrderNo",
            "skuCode",
            // MAN-445/#799: 库存过账失败原因,支撑 Console 失败行红 badge + 失败原因文案 + 行内重试。
            "inventoryPostingFailureCode",
            "inventoryPostingFailureMessage",
            "inventoryPostingFailedAtUtc");
        AssertMesStatusEnum(document, "BusinessConsoleMesReceiptRequestRow", "receiptStatus");

        // MAN-445/#799: 工单详情活跃质量保留投影,支撑 hold 区块时间线定位键(sourceService+sourceDocumentId)+强制释放。
        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesWorkOrderQualityHoldSummary",
            "sourceService",
            "sourceDocumentId",
            "scope",
            "isActive",
            "holdReason",
            "heldAtUtc",
            "heldBy",
            // 已释放周期审计,支撑释放后详情面板常驻展示释放时间/方式(issue 验收「时间线完整」)。
            "releasedAtUtc",
            "releasedBy",
            "releaseReason",
            "releaseSource");
        // MAN-445/#799: 工单列表锁定标志,支撑质量保留中工单的锁定图标。
        AssertMesDisplayProperties(
            document,
            "BusinessConsoleMesWorkOrderItem",
            "hasActiveQualityHold");
    }

    private static void AssertMesDisplayProperties(JsonDocument document, string schemaNameSuffix, params string[] propertyNames)
    {
        var schema = FindSchemaBySuffix(document, schemaNameSuffix);
        var properties = schema.GetProperty("properties");
        foreach (var propertyName in propertyNames)
        {
            Assert.True(
                properties.TryGetProperty(propertyName, out _),
                $"{schemaNameSuffix} must expose {propertyName} so the frontend does not render raw internal ids.");
        }
    }

    private static void AssertMesStatusEnum(JsonDocument document, string schemaNameSuffix, string propertyName)
    {
        var property = FindSchemaBySuffix(document, schemaNameSuffix)
            .GetProperty("properties")
            .GetProperty(propertyName);

        Assert.True(
            property.TryGetProperty("enum", out var inlineEnum)
            || property.TryGetProperty("$ref", out _)
            || property.TryGetProperty("oneOf", out _),
            $"{schemaNameSuffix}.{propertyName} must be an OpenAPI enum, not a free-form string.");

        if (property.TryGetProperty("enum", out inlineEnum))
        {
            Assert.Contains(inlineEnum.EnumerateArray(), value => value.GetString() == "ready");
            Assert.Contains(inlineEnum.EnumerateArray(), value => value.GetString() == "posted");
        }
    }

    private static void AssertMesStatusQueryEnum(JsonElement paths, string path)
    {
        var statusParameter = FindQueryParameter(paths, path, "get", "status");
        var schema = statusParameter.GetProperty("schema");

        Assert.True(
            schema.TryGetProperty("enum", out var values),
            $"{path} status query parameter must be an OpenAPI enum, not a free-form string.");
        Assert.Contains(values.EnumerateArray(), value => value.GetString() == "ready");
        Assert.Contains(values.EnumerateArray(), value => value.GetString() == "posted");
    }

    private static JsonElement FindSchemaBySuffix(JsonDocument document, string schemaNameSuffix)
    {
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(schema => schema.Name.EndsWith(schemaNameSuffix, StringComparison.Ordinal))
            .ToArray();

        Assert.Single(schemas);
        return schemas[0].Value;
    }

    private static void AssertRequiredSchemaProperty(JsonDocument document, string schemaNameSuffix, string propertyName)
    {
        var schema = FindSchemaBySuffix(document, schemaNameSuffix);
        var required = schema.GetProperty("required");

        Assert.Contains(required.EnumerateArray(), value => value.GetString() == propertyName);
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
