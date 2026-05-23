namespace Nerv.IIP.Business.Acceptance.Tests;

public static class BusinessFullChainAcceptanceSurface
{
    public static IReadOnlyCollection<BusinessChainAcceptanceSurface> Chains { get; } =
    [
        Chain("#77 harness baseline: Engineering to manufacturing", [
            Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/skus", "createBusinessMasterDataSku"),
            Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/work-centers", "createBusinessMasterDataWorkCenter"),
            Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/engineering-boms/release", "releaseBusinessEngineeringBom"),
            Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/manufacturing-boms/release", "releaseBusinessManufacturingBom"),
            Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/routings/release", "releaseBusinessRouting"),
            Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/production-versions", "createBusinessProductionVersion"),
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/mrp-runs", "runPlanningMrp"),
            Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
            Endpoint("BusinessMes", "GET", "/api/business/v1/mes/work-orders", "listBusinessMesWorkOrders"),
        ]),
        Chain("#77 harness baseline: Plan to procure/produce", [
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/demands", "createOrUpdatePlanningDemand"),
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/mrp-runs", "runPlanningMrp"),
            Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/suggestions", "listPlanningSuggestions"),
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/suggestions/{suggestionId}/accept", "acceptPlanningSuggestion"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-requisitions/from-suggestion", "createErpPurchaseRequisitionFromSuggestion"),
            Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
        ]),
        Chain("#77 harness baseline: Procure to pay", [
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/rfqs", "createErpRequestForQuotation"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/supplier-quotations", "receiveErpSupplierQuotation"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-orders", "createErpPurchaseOrder"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-receipts", "recordErpPurchaseReceipt"),
            Endpoint("BusinessQuality", "POST", "/api/business/v1/quality/inspection-records", "createBusinessQualityInspectionRecord"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders", "createWmsInboundOrder"),
            Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/finance/payables", "createErpAccountPayable"),
        ]),
        Chain("#77 harness baseline: Order to cash", [
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/opportunities", "openErpOpportunity"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/quotations", "createErpQuotation"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/sales-orders", "createErpSalesOrder"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/delivery-orders", "releaseErpDeliveryOrder"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders", "createWmsOutboundOrder"),
            Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/finance/receivables", "createErpAccountReceivable"),
        ]),
        Chain("#77 harness baseline: Production to cost", [
            Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
            Endpoint("BusinessQuality", "POST", "/api/business/v1/quality/inspection-records", "createBusinessQualityInspectionRecord"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders", "createWmsInboundOrder"),
            Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/finance/cost-candidates", "createErpCostCandidate"),
        ]),
        Chain("#77 harness baseline: Equipment to maintenance", [
            Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/device-assets", "registerBusinessMasterDataDeviceAsset"),
            Endpoint("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/tags", "createBusinessIiotTelemetryTag"),
            Endpoint("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/alarms", "raiseBusinessIiotAlarm"),
            Endpoint("BusinessMaintenance", "POST", "/api/business/v1/maintenance/work-orders", "createMaintenanceWorkOrder"),
            Endpoint("BusinessMaintenance", "POST", "/api/business/v1/maintenance/work-orders/{workOrderId}/complete", "completeMaintenanceWorkOrder"),
            Endpoint("BusinessMes", "POST", "/api/business/v1/mes/schedules/run", "runBusinessMesSchedule"),
        ]),
        Chain("#77 harness baseline: WCS adapter", [
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", "dispatchWmsWcsTask"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", "failWmsWcsTask"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete", "completeWmsWcsTask"),
            Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
        ]),
    ];

    private static BusinessChainAcceptanceSurface Chain(string chainName, IReadOnlyCollection<EndpointSurface> requiredEndpoints)
    {
        return new BusinessChainAcceptanceSurface(chainName, requiredEndpoints);
    }

    private static EndpointSurface Endpoint(string service, string httpMethod, string route, string operationId)
    {
        return new EndpointSurface(service, httpMethod, route, operationId);
    }
}

public sealed record BusinessChainAcceptanceSurface(
    string ChainName,
    IReadOnlyCollection<EndpointSurface> RequiredEndpoints);

public sealed record EndpointSurface(
    string Service,
    string HttpMethod,
    string Route,
    string OperationId);
