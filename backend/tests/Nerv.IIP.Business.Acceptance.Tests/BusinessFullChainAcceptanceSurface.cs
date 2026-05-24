namespace Nerv.IIP.Business.Acceptance.Tests;

public static class BusinessFullChainAcceptanceSurface
{
    public static IReadOnlyCollection<BusinessChainAcceptanceSurface> Chains { get; } =
    [
        Chain(
            "#77 harness baseline: Engineering to manufacturing",
            [
                Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/skus", "createBusinessMasterDataSku"),
                Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/work-centers", "createBusinessMasterDataWorkCenter"),
                Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/references/resolve", "resolveBusinessMasterDataReferences"),
                Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/engineering-boms/release", "releaseBusinessEngineeringBom"),
                Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/manufacturing-boms/release", "releaseBusinessManufacturingBom"),
                Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/routings/release", "releaseBusinessRouting"),
                Endpoint("BusinessProductEngineering", "POST", "/api/business/v1/engineering/production-versions", "createBusinessProductionVersion"),
                Endpoint("BusinessProductEngineering", "GET", "/api/business/v1/engineering/production-versions/resolve", "resolveBusinessProductionVersion"),
                Endpoint("BusinessProductEngineering", "GET", "/api/business/v1/engineering/production-versions", "listBusinessProductionVersions"),
                Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
                Endpoint("BusinessMes", "GET", "/api/business/v1/mes/work-orders", "listBusinessMesWorkOrders"),
            ],
            [
                EventFact("BusinessMasterData", "masterData.ReferencesResolved", "master-data-reference-resolution"),
                EventFact("BusinessProductEngineering", "productEngineering.EngineeringBomReleased", "engineering-bom"),
                EventFact("BusinessProductEngineering", "productEngineering.ManufacturingBomReleased", "manufacturing-bom"),
                EventFact("BusinessProductEngineering", "productEngineering.RoutingReleased", "routing"),
                EventFact("BusinessProductEngineering", "productEngineering.ProductionVersionCreated", "production-version"),
                EventFact("BusinessMes", "mes.WorkOrderCreated", "work-order"),
            ],
            [
                VisibleFact("master-data-reference-resolution", "BusinessMasterData", "SKU and work center references are resolved through the MasterData public resolve endpoint."),
                VisibleFact("production-version", "BusinessProductEngineering", "Production version id, SKU, MBOM version and routing version are visible through ProductEngineering public endpoints or event payloads."),
                VisibleFact("work-order", "BusinessMes", "MES work order id, SKU and production version id are visible through MES public endpoints or event payloads."),
            ]),
        Chain(
            "#77 harness baseline: Plan to procure/produce",
            [
                Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/demands", "createOrUpdatePlanningDemand"),
                Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/mrp-runs", "runPlanningMrp"),
                Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/mrp-runs", "listPlanningMrpRuns"),
                Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/mrp-runs/{runId}/pegging", "getPlanningMrpPegging"),
                Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/suggestions", "listPlanningSuggestions"),
                Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/suggestions/{suggestionId}/accept", "acceptPlanningSuggestion"),
                Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-requisitions/from-suggestion", "createErpPurchaseRequisitionFromSuggestion"),
                Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
                Endpoint("BusinessMes", "GET", "/api/business/v1/mes/work-orders", "listBusinessMesWorkOrders"),
            ],
            [
                EventFact("BusinessDemandPlanning", "demandPlanning.MrpRunCompleted", "mrp-run"),
                EventFact("BusinessDemandPlanning", "demandPlanning.PlannedPurchaseSuggested", "planned-purchase-suggestion"),
                EventFact("BusinessDemandPlanning", "demandPlanning.PlannedWorkOrderSuggested", "planned-work-order-suggestion"),
                EventFact("BusinessDemandPlanning", "demandPlanning.PlanningSuggestionAccepted", "planning-suggestion-acceptance"),
                EventFact("BusinessErp", "erp.PurchaseRequisitionCreated", "purchase-requisition"),
                EventFact("BusinessMes", "mes.WorkOrderCreated", "work-order"),
            ],
            [
                VisibleFact("mrp-run", "BusinessDemandPlanning", "MRP run id, source demand and pegging are visible through DemandPlanning public endpoints or event payloads."),
                VisibleFact("planned-purchase-suggestion", "BusinessDemandPlanning", "Planned purchase suggestion id, demand source and SKU are visible through DemandPlanning public endpoints or event payloads."),
                VisibleFact("planned-work-order-suggestion", "BusinessDemandPlanning", "Planned work order suggestion id and production version reference are visible through DemandPlanning public endpoints or event payloads."),
                VisibleFact("purchase-requisition", "BusinessErp", "ERP purchase requisition id and source planning suggestion are visible through ERP public endpoints or event payloads."),
                VisibleFact("work-order", "BusinessMes", "MES work order id, source planning suggestion and production version id are visible through MES public endpoints or event payloads."),
            ]),
        Chain(
            "#77 harness baseline: Procure to pay",
            CommercialFinanceAcceptanceData.ProcureToPayEndpoints,
            [
                EventFact("BusinessErp", "erp.PurchaseRequisitionCreated", "purchase-requisition"),
                EventFact("BusinessErp", "erp.PurchaseOrderReleased", "purchase-order"),
                EventFact("BusinessErp", "erp.PurchaseReceiptRecorded", "purchase-receipt"),
                EventFact("BusinessQuality", "quality.InspectionPassed", "quality-inspection"),
                EventFact("BusinessWms", "wms.InboundOrderCompleted", "wms-inbound-completion"),
                EventFact("BusinessInventory", "inventory.StockMovementPosted", "inventory-movement"),
                EventFact("BusinessErp", "erp.AccountPayableCreated", "account-payable"),
            ],
            [
                VisibleFact("purchase-requisition", "BusinessErp", "Purchase requisition id and source suggestion are visible through ERP public endpoints or event payloads."),
                VisibleFact("request-for-quotation", "BusinessErp", "RFQ id, supplier scope, item, quantity and due date are visible through ERP public endpoints or event payloads."),
                VisibleFact("supplier-quotation", "BusinessErp", "Supplier quotation id, supplier, quoted price and currency are visible through ERP public endpoints or event payloads."),
                VisibleFact("purchase-order", "BusinessErp", "Purchase order id, supplier, item, quantity and promised date are visible through ERP public endpoints or event payloads."),
                VisibleFact("purchase-receipt", "BusinessErp", "Purchase receipt id, purchase order id, accepted quantity and receipt date are visible through ERP public endpoints or event payloads."),
                VisibleFact("quality-inspection", "BusinessQuality", "Inspection record id, source receipt, inspected quantity and result are visible through Quality public endpoints or event payloads."),
                VisibleFact("wms-inbound-completion", "BusinessWms", "Inbound order completion id, source receipt and completed quantity are visible through WMS public endpoints or event payloads."),
                VisibleFact("inventory-availability", "BusinessInventory", "Stock availability is asserted through /api/inventory/v1/availability, not by reading the Inventory database."),
                VisibleFact("finance-summary", "BusinessErp", "Finance totals are asserted through /api/business/v1/erp/finance/summary, not by reading ERP tables."),
                VisibleFact("account-payable-source-document", "BusinessErp", "AP candidate amount and source receipt are asserted through /api/business/v1/erp/finance/payables/by-source."),
            ]),
        Chain(
            "#77 harness baseline: Order to cash",
            CommercialFinanceAcceptanceData.OrderToCashEndpoints,
            [
                EventFact("BusinessErp", "erp.DeliveryOrderReleased", "delivery-order"),
                EventFact("BusinessWms", "wms.OutboundOrderCompleted", "wms-outbound-completion"),
                EventFact("BusinessInventory", "inventory.StockMovementPosted", "inventory-movement"),
                EventFact("BusinessErp", "erp.AccountReceivableCreated", "account-receivable"),
            ],
            [
                VisibleFact("opportunity", "BusinessErp", "Opportunity id, customer and expected item are visible through ERP public endpoints or event payloads."),
                VisibleFact("quotation", "BusinessErp", "Quotation id, opportunity id, customer, item, quantity and amount are visible through ERP public endpoints or event payloads."),
                VisibleFact("quotation-approval", "BusinessErp", "Quotation approval is visible through the ERP approval endpoint and event payloads."),
                VisibleFact("sales-order", "BusinessErp", "Sales order id, customer, item, quantity and requested date are visible through ERP public endpoints or event payloads."),
                VisibleFact("delivery-order", "BusinessErp", "Delivery order id, source sales order and requested delivery quantity are visible through ERP public endpoints or event payloads."),
                VisibleFact("wms-outbound-completion", "BusinessWms", "Outbound order completion id, source delivery order and shipped quantity are visible through WMS public endpoints or event payloads."),
                VisibleFact("inventory-availability", "BusinessInventory", "Stock availability is asserted through /api/inventory/v1/availability, not by reading the Inventory database."),
                VisibleFact("finance-summary", "BusinessErp", "Finance totals are asserted through /api/business/v1/erp/finance/summary, not by reading ERP tables."),
                VisibleFact("account-receivable-source-document", "BusinessErp", "AR candidate amount and source delivery are asserted through /api/business/v1/erp/finance/receivables/by-source."),
            ]),
        Chain(
            "#77 harness baseline: Production to cost",
            CommercialFinanceAcceptanceData.ProductionToCostEndpoints,
            [
                EventFact("BusinessQuality", "quality.InspectionPassed", "quality-inspection"),
                EventFact("BusinessWms", "wms.InboundOrderCompleted", "wms-inbound-completion"),
                EventFact("BusinessInventory", "inventory.StockMovementPosted", "inventory-movement"),
                EventFact("BusinessErp", "erp.CostCandidateCreated", "cost-candidate"),
            ],
            [
                VisibleFact("work-order", "BusinessMes", "Work order id, SKU, quantity and due date are visible through MES public endpoints or event payloads."),
                VisibleFact("quality-inspection", "BusinessQuality", "Inspection record id, source work order, inspected quantity and result are visible through Quality public endpoints or event payloads."),
                VisibleFact("wms-inbound-completion", "BusinessWms", "Inbound completion id, source work order and completed quantity are visible through WMS public endpoints or event payloads."),
                VisibleFact("inventory-movement", "BusinessInventory", "Inventory movement id, source work order and posted quantity are visible through Inventory public endpoint responses or event payloads."),
                VisibleFact("inventory-availability", "BusinessInventory", "Stock availability is asserted through /api/inventory/v1/availability, not by reading the Inventory database."),
                VisibleFact("production-report", "BusinessMes", "Production report quantity and operation completion are visible through MES public production report endpoints."),
                VisibleFact("finished-goods-receipt-request", "BusinessMes", "Finished-goods receipt request references are visible through MES public receipt request endpoints."),
                VisibleFact("cost-candidate", "BusinessErp", "Cost candidate creation and source document drill-down are visible through ERP public finance endpoints."),
                VisibleFact("finance-summary", "BusinessErp", "Finance totals are asserted through /api/business/v1/erp/finance/summary, not by reading ERP tables."),
            ]),
        Chain("#77 harness baseline: Equipment to maintenance", [
            Endpoint("BusinessMasterData", "POST", "/api/business/v1/master-data/device-assets", "registerBusinessMasterDataDeviceAsset"),
            Endpoint("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/tags", "createBusinessIiotTelemetryTag"),
            Endpoint("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/samples", "recordBusinessIiotTelemetrySample"),
            Endpoint("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/alarms", "raiseBusinessIiotAlarm"),
            Endpoint("BusinessIndustrialTelemetry", "GET", "/api/business/v1/iiot/alarms", "listBusinessIiotAlarms"),
            Endpoint("BusinessIndustrialTelemetry", "GET", "/api/business/v1/iiot/devices/{deviceAssetId}/timeline", "queryBusinessIiotDeviceTimeline"),
            Endpoint("BusinessMaintenance", "POST", "/api/business/v1/maintenance/work-orders", "createMaintenanceWorkOrder"),
            Endpoint("BusinessMaintenance", "GET", "/api/business/v1/maintenance/work-orders", "listMaintenanceWorkOrders"),
            Endpoint("BusinessMaintenance", "POST", "/api/business/v1/maintenance/work-orders/{workOrderId}/complete", "completeMaintenanceWorkOrder"),
            Endpoint("BusinessMes", "POST", "/api/business/v1/mes/schedules/run", "runBusinessMesSchedule"),
            Endpoint("BusinessMes", "GET", "/api/business/v1/mes/capacity-impacts", "listBusinessMesCapacityImpacts"),
        ],
            [
                EventFact("BusinessIndustrialTelemetry", "industrialTelemetry.AlarmRaised", "alarm-raised"),
                EventFact("BusinessMaintenance", "maintenance.AssetUnavailable", "asset-unavailable"),
                EventFact("BusinessMaintenance", "maintenance.AssetRestored", "asset-restored"),
            ],
            [
                VisibleFact("alarm-raised", "BusinessIndustrialTelemetry", "AlarmRaised is asserted through the public alarm list and device timeline, not by reading IIoT tables."),
                VisibleFact("asset-unavailable", "BusinessMaintenance", "AssetUnavailable is asserted through the public maintenance work-order list and event recorder."),
                VisibleFact("asset-restored", "BusinessMaintenance", "AssetRestored is asserted through the public maintenance work-order list and event recorder."),
                VisibleFact("mes-capacity-impact", "BusinessMes", "MES capacity impact is asserted by public capacity impact query after maintenance events, not by cross-service database reads."),
            ]),
        Chain("#77 harness baseline: WCS adapter", [
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders", "createWmsInboundOrder"),
            Endpoint("BusinessWms", "GET", "/api/business/v1/wms/inbound-orders", "listWmsInboundOrders"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks", "createWmsPutawayTask"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", "completeWmsInboundOrder"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders", "createWmsOutboundOrder"),
            Endpoint("BusinessWms", "GET", "/api/business/v1/wms/outbound-orders", "listWmsOutboundOrders"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", "createWmsPickingTask"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete", "completeWmsOutboundOrder"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", "dispatchWmsWcsTask"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", "failWmsWcsTask"),
            Endpoint("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete", "completeWmsWcsTask"),
            Endpoint("BusinessWms", "GET", "/api/business/v1/wms/wcs-tasks", "listWmsWcsTasks"),
            Endpoint("BusinessInventory", "GET", "/api/inventory/v1/availability", "getInventoryAvailability"),
            Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
        ],
            [
                EventFact("BusinessWms", "wms.WcsTaskDispatched", "wcs-dispatch"),
                EventFact("BusinessWms", "wms.WcsTaskFailed", "wcs-failure"),
                EventFact("BusinessWms", "wms.WcsTaskCompleted", "wcs-completion"),
                EventFact("BusinessInventory", "inventory.StockAvailabilityChanged", "inventory-movement-gate"),
            ],
            [
                VisibleFact("wcs-dispatch", "BusinessWms", "WCS dispatch is asserted through the public dispatch command surface and event recorder."),
                VisibleFact("wcs-failure", "BusinessWms", "WCS failure diagnostics are asserted through the public WCS task query and event recorder."),
                VisibleFact("wcs-completion", "BusinessWms", "WCS completion is asserted through the public WCS task query and event recorder."),
                VisibleFact("inventory-movement-gate", "BusinessInventory", "Inventory availability is asserted unchanged before WMS completion and changed only after inbound/outbound completion posts movement."),
            ]),
    ];

    private static BusinessChainAcceptanceSurface Chain(
        string chainName,
        IReadOnlyCollection<EndpointSurface> requiredEndpoints,
        IReadOnlyCollection<AcceptanceEventFact>? eventRecorderFacts = null,
        IReadOnlyCollection<VisibleAcceptanceFact>? visibleFacts = null,
        IReadOnlyCollection<AcceptanceRisk>? knownRisks = null)
    {
        return new BusinessChainAcceptanceSurface(
            chainName,
            requiredEndpoints,
            eventRecorderFacts ?? [],
            visibleFacts ?? [],
            knownRisks ?? []);
    }

    private static EndpointSurface Endpoint(string service, string httpMethod, string route, string operationId)
    {
        return new EndpointSurface(service, httpMethod, route, operationId);
    }

    private static AcceptanceEventFact EventFact(string service, string eventType, string factKey)
    {
        return new AcceptanceEventFact(service, eventType, factKey);
    }

    private static VisibleAcceptanceFact VisibleFact(string factKey, string service, string statement)
    {
        return new VisibleAcceptanceFact(factKey, service, statement, RequiresCrossServiceDatabaseRead: false);
    }

    private static AcceptanceRisk Risk(string riskId, string service, string statement)
    {
        return new AcceptanceRisk(riskId, service, statement);
    }
}

public sealed record BusinessChainAcceptanceSurface(
    string ChainName,
    IReadOnlyCollection<EndpointSurface> RequiredEndpoints,
    IReadOnlyCollection<AcceptanceEventFact> EventRecorderFacts,
    IReadOnlyCollection<VisibleAcceptanceFact> VisibleFacts,
    IReadOnlyCollection<AcceptanceRisk> KnownRisks);

public sealed record EndpointSurface(
    string Service,
    string HttpMethod,
    string Route,
    string OperationId);

public sealed record AcceptanceEventFact(
    string Service,
    string EventType,
    string FactKey);

public sealed record VisibleAcceptanceFact(
    string FactKey,
    string Service,
    string Statement,
    bool RequiresCrossServiceDatabaseRead);

public sealed record AcceptanceRisk(
    string RiskId,
    string Service,
    string Statement);
