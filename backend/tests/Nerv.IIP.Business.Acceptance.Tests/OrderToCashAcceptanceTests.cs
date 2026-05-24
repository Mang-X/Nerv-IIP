namespace Nerv.IIP.Business.Acceptance.Tests;

// Metadata-only acceptance surface checks; this chain does not need shared fixture state yet.
public sealed class OrderToCashAcceptanceTests
{
    [Fact]
    public void Order_to_cash_surface_exposes_source_to_finance_public_endpoints()
    {
        var chain = GetChain();

        AssertSuperset(chain.RequiredEndpoints, CommercialFinanceAcceptanceData.OrderToCashEndpoints);
        Assert.All(chain.RequiredEndpoints, endpoint => Assert.Contains(endpoint, PublicBusinessEndpointCatalog.All));
    }

    [Fact]
    public void Order_to_cash_records_events_and_visible_facts_without_cross_service_database_reads()
    {
        var chain = GetChain();

        AssertExpectedEvents(
            chain,
            "erp.DeliveryOrderReleased",
            "wms.OutboundOrderCompleted",
            "inventory.StockMovementPosted",
            "erp.AccountReceivableCreated");

        AssertVisibleFacts(
            chain,
            "opportunity",
            "quotation",
            "quotation-approval",
            "sales-order",
            "delivery-order",
            "wms-outbound-completion",
            "inventory-availability",
            "finance-summary");
    }

    [Fact]
    public void Order_to_cash_has_source_document_level_receivable_drill_down()
    {
        var chain = GetChain();

        Assert.DoesNotContain(chain.KnownRisks, risk => risk.RiskId == "erp-finance-source-document-receivable-query");
        Assert.Contains(
            new EndpointSurface("BusinessErp", "GET", "/api/business/v1/erp/finance/receivables/by-source", "getErpReceivableBySourceDocument"),
            chain.RequiredEndpoints);
    }

    private static BusinessChainAcceptanceSurface GetChain()
    {
        return Assert.Single(BusinessFullChainAcceptanceSurface.Chains, chain => chain.ChainName == CommercialFinanceAcceptanceData.OrderToCash);
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
