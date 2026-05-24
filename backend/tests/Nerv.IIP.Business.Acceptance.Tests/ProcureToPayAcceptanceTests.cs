namespace Nerv.IIP.Business.Acceptance.Tests;

// Metadata-only acceptance surface checks; this chain does not need shared fixture state yet.
public sealed class ProcureToPayAcceptanceTests
{
    [Fact]
    public void Procure_to_pay_surface_exposes_source_to_finance_public_endpoints()
    {
        var chain = GetChain();

        AssertSuperset(chain.RequiredEndpoints, CommercialFinanceAcceptanceData.ProcureToPayEndpoints);
        Assert.All(chain.RequiredEndpoints, endpoint => Assert.Contains(endpoint, PublicBusinessEndpointCatalog.All));
    }

    [Fact]
    public void Procure_to_pay_records_events_and_visible_facts_without_cross_service_database_reads()
    {
        var chain = GetChain();

        AssertExpectedEvents(
            chain,
            "erp.PurchaseRequisitionCreated",
            "erp.PurchaseOrderReleased",
            "erp.PurchaseReceiptRecorded",
            "quality.InspectionPassed",
            "wms.InboundOrderCompleted",
            "inventory.StockMovementPosted",
            "erp.AccountPayableCreated");

        AssertVisibleFacts(
            chain,
            "purchase-requisition",
            "request-for-quotation",
            "supplier-quotation",
            "purchase-order",
            "purchase-receipt",
            "quality-inspection",
            "wms-inbound-completion",
            "inventory-availability",
            "finance-summary");
    }

    [Fact]
    public void Procure_to_pay_documents_public_query_gap_for_source_document_level_payables()
    {
        var chain = GetChain();

        Assert.Contains(chain.KnownRisks, risk =>
            risk.RiskId == "erp-finance-source-document-payable-query"
            && risk.Service == "BusinessErp"
            && risk.Statement.Contains("payable", StringComparison.OrdinalIgnoreCase));
    }

    private static BusinessChainAcceptanceSurface GetChain()
    {
        return Assert.Single(BusinessFullChainAcceptanceSurface.Chains, chain => chain.ChainName == CommercialFinanceAcceptanceData.ProcureToPay);
    }

    private static void AssertSuperset(IReadOnlyCollection<EndpointSurface> actual, IReadOnlyCollection<EndpointSurface> expected)
    {
        var missing = expected.Where(endpoint => !actual.Contains(endpoint)).ToArray();
        Assert.Empty(missing);
    }

    private static void AssertExpectedEvents(BusinessChainAcceptanceSurface chain, params string[] eventTypes)
    {
        Assert.All(eventTypes, eventType => Assert.Contains(chain.EventRecorderFacts, fact => fact.EventType == eventType));
    }

    private static void AssertVisibleFacts(BusinessChainAcceptanceSurface chain, params string[] factKeys)
    {
        Assert.All(factKeys, factKey => Assert.Contains(chain.VisibleFacts, fact => fact.FactKey == factKey));
        Assert.All(chain.VisibleFacts, fact => Assert.False(fact.RequiresCrossServiceDatabaseRead));
    }
}
