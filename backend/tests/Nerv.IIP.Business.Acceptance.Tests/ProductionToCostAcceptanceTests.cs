namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class ProductionToCostAcceptanceTests
{
    [Fact]
    public void Production_to_cost_surface_exposes_source_to_finance_public_endpoints()
    {
        var chain = GetChain();

        AssertSuperset(chain.RequiredEndpoints, CommercialFinanceAcceptanceData.ProductionToCostEndpoints);
        Assert.All(chain.RequiredEndpoints, endpoint => Assert.Contains(endpoint, PublicBusinessEndpointCatalog.All));
    }

    [Fact]
    public void Production_to_cost_records_events_and_visible_facts_without_cross_service_database_reads()
    {
        var chain = GetChain();

        AssertExpectedEvents(
            chain,
            "quality.InspectionPassed",
            "wms.InboundOrderCompleted",
            "inventory.StockMovementPosted",
            "erp.CostCandidateCreated");

        AssertVisibleFacts(
            chain,
            "work-order",
            "quality-inspection",
            "wms-inbound-completion",
            "inventory-movement",
            "inventory-availability",
            "cost-candidate",
            "finance-summary");
    }

    [Fact]
    public void Production_to_cost_documents_public_query_gap_for_source_document_level_cost_candidates()
    {
        var chain = GetChain();

        Assert.Contains(chain.KnownRisks, risk =>
            risk.RiskId == "erp-finance-source-document-cost-candidate-query"
            && risk.Service == "BusinessErp"
            && risk.Statement.Contains("cost candidate", StringComparison.OrdinalIgnoreCase));
    }

    private static BusinessChainAcceptanceSurface GetChain()
    {
        return Assert.Single(BusinessFullChainAcceptanceSurface.Chains, chain => chain.ChainName == CommercialFinanceAcceptanceData.ProductionToCost);
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
