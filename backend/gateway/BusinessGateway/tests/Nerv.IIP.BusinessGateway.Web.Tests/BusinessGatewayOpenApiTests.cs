using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
        AssertOperationId(paths, "/api/business-console/v1/inventory/availability", "get", "getBusinessConsoleInventoryAvailability");
        AssertOperationId(paths, "/api/business-console/v1/inventory/movements", "post", "postBusinessConsoleInventoryMovement");
        AssertOperationId(paths, "/api/business-console/v1/inventory/count-tasks", "post", "createBusinessConsoleInventoryCountTask");
        AssertOperationId(paths, "/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments", "post", "confirmBusinessConsoleInventoryCountAdjustment");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-plans", "get", "listBusinessConsoleQualityInspectionPlans");
        AssertOperationId(paths, "/api/business-console/v1/quality/inspection-records", "post", "createBusinessConsoleQualityInspectionRecord");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs", "get", "listBusinessConsoleQualityNcrs");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/disposition", "post", "submitBusinessConsoleQualityNcrDisposition");
        AssertOperationId(paths, "/api/business-console/v1/quality/ncrs/{ncrId}/close", "post", "closeBusinessConsoleQualityNcr");
        AssertOperationId(paths, "/api/business-console/v1/engineering/engineering-boms", "get", "listBusinessConsoleEngineeringBoms");
        AssertOperationId(paths, "/api/business-console/v1/engineering/routings", "get", "listBusinessConsoleEngineeringRoutings");
        AssertOperationId(paths, "/api/business-console/v1/engineering/production-versions", "get", "listBusinessConsoleEngineeringProductionVersions");
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
        AssertOperationId(paths, "/api/business-console/v1/erp/procurement/purchase-orders", "get", "listBusinessConsoleErpPurchaseOrders");
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
}
