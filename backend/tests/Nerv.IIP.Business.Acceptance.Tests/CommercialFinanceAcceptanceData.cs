namespace Nerv.IIP.Business.Acceptance.Tests;

public static class CommercialFinanceAcceptanceData
{
    public const string ProcureToPay = "#77 harness baseline: Procure to pay";
    public const string OrderToCash = "#77 harness baseline: Order to cash";
    public const string ProductionToCost = "#77 harness baseline: Production to cost";

    public static IReadOnlyCollection<string> CommercialFinanceChainNames { get; } =
    [
        ProcureToPay,
        OrderToCash,
        ProductionToCost,
    ];

    public static IReadOnlyCollection<EndpointSurface> ProcureToPayEndpoints { get; } =
    [
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-requisitions/from-suggestion", "createErpPurchaseRequisitionFromSuggestion"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/rfqs", "createErpRequestForQuotation"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/supplier-quotations", "receiveErpSupplierQuotation"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-orders", "createErpPurchaseOrder"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-receipts", "recordErpPurchaseReceipt"),
        Endpoint("BusinessQuality", "POST", "/api/business/v1/quality/inspection-records", "createBusinessQualityInspectionRecord"),
        Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders", "createWmsInboundOrder"),
        Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", "completeWmsInboundOrder"),
        Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
        Endpoint("BusinessInventory", "GET", "/api/inventory/v1/availability", "getInventoryAvailability"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/finance/payables", "createErpAccountPayable"),
        Endpoint("BusinessErp", "GET", "/api/business/v1/erp/finance/payables/by-source", "getErpPayableBySourceDocument"),
        Endpoint("BusinessErp", "GET", "/api/business/v1/erp/finance/summary", "getErpFinanceSummary"),
    ];

    public static IReadOnlyCollection<EndpointSurface> OrderToCashEndpoints { get; } =
    [
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/opportunities", "openErpOpportunity"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/quotations", "createErpQuotation"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/quotations/{quotationId}/approve", "approveErpQuotation"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/sales-orders", "createErpSalesOrder"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/delivery-orders", "releaseErpDeliveryOrder"),
        Endpoint("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders", "createWmsOutboundOrder"),
        Endpoint("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete", "completeWmsOutboundOrder"),
        Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
        Endpoint("BusinessInventory", "GET", "/api/inventory/v1/availability", "getInventoryAvailability"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/finance/receivables", "createErpAccountReceivable"),
        Endpoint("BusinessErp", "GET", "/api/business/v1/erp/finance/receivables/by-source", "getErpReceivableBySourceDocument"),
        Endpoint("BusinessErp", "GET", "/api/business/v1/erp/finance/summary", "getErpFinanceSummary"),
    ];

    public static IReadOnlyCollection<EndpointSurface> ProductionToCostEndpoints { get; } =
    [
        Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
        Endpoint("BusinessMes", "POST", "/api/business/v1/mes/production-reports", "recordBusinessMesProductionReport"),
        Endpoint("BusinessMes", "GET", "/api/business/v1/mes/production-reports", "listBusinessMesProductionReports"),
        Endpoint("BusinessMes", "POST", "/api/business/v1/mes/finished-goods-receipt-requests", "createBusinessMesFinishedGoodsReceiptRequest"),
        Endpoint("BusinessMes", "GET", "/api/business/v1/mes/finished-goods-receipt-requests", "listBusinessMesFinishedGoodsReceiptRequests"),
        Endpoint("BusinessQuality", "POST", "/api/business/v1/quality/inspection-records", "createBusinessQualityInspectionRecord"),
        Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders", "createWmsInboundOrder"),
        Endpoint("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", "completeWmsInboundOrder"),
        Endpoint("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
        Endpoint("BusinessInventory", "GET", "/api/inventory/v1/availability", "getInventoryAvailability"),
        Endpoint("BusinessErp", "POST", "/api/business/v1/erp/finance/cost-candidates", "createErpCostCandidate"),
        Endpoint("BusinessErp", "GET", "/api/business/v1/erp/finance/cost-candidates/by-source", "getErpCostCandidateBySourceDocument"),
        Endpoint("BusinessErp", "GET", "/api/business/v1/erp/finance/summary", "getErpFinanceSummary"),
    ];

    private static EndpointSurface Endpoint(string service, string httpMethod, string route, string operationId)
    {
        return new EndpointSurface(service, httpMethod, route, operationId);
    }
}
