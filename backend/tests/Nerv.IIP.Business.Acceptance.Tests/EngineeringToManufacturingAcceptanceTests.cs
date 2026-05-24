namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class EngineeringToManufacturingAcceptanceTests
{
    private readonly BusinessAcceptanceFixture _fixture;

    public EngineeringToManufacturingAcceptanceTests(BusinessAcceptanceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Engineering_to_manufacturing_surface_includes_master_data_engineering_and_mes_public_endpoints()
    {
        var chain = FindChain("#77 harness baseline: Engineering to manufacturing");
        var requiredEndpoints = new[]
        {
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
        };

        Assert.All(requiredEndpoints, endpoint => Assert.Contains(endpoint, chain.RequiredEndpoints));
        Assert.All(requiredEndpoints, endpoint => Assert.Contains(endpoint, PublicBusinessEndpointCatalog.All));
    }

    [Fact]
    public void Engineering_to_manufacturing_visible_facts_join_production_version_to_mbom_routing_and_mes_work_order()
    {
        var correlation = _fixture.BeginCorrelation("engineering-to-manufacturing");

        _fixture.Events.Record("BusinessMasterData", "masterData.ReferencesResolved", correlation, new
        {
            EngineeringPlanningAcceptanceData.SkuCode,
            EngineeringPlanningAcceptanceData.WorkCenterCode,
        });
        _fixture.Events.Record("BusinessProductEngineering", "productEngineering.ProductionVersionCreated", correlation, new
        {
            EngineeringPlanningAcceptanceData.ProductionVersionId,
            EngineeringPlanningAcceptanceData.SkuCode,
            EngineeringPlanningAcceptanceData.MbomVersionId,
            EngineeringPlanningAcceptanceData.RoutingVersionId,
        });
        _fixture.Events.Record("BusinessMes", "mes.WorkOrderCreated", correlation, new
        {
            EngineeringPlanningAcceptanceData.WorkOrderId,
            EngineeringPlanningAcceptanceData.SkuCode,
            EngineeringPlanningAcceptanceData.ProductionVersionId,
        });

        var events = _fixture.Events.ForCorrelation(correlation.CorrelationId);
        var productionVersion = SingleEvent(events, "BusinessProductEngineering", "productEngineering.ProductionVersionCreated");
        var workOrder = SingleEvent(events, "BusinessMes", "mes.WorkOrderCreated");

        var productionVersionFacts = EngineeringPlanningAcceptanceData.VisibleFacts(productionVersion);
        var workOrderFacts = EngineeringPlanningAcceptanceData.VisibleFacts(workOrder);

        Assert.Equal(
            EngineeringPlanningAcceptanceData.MbomVersionId,
            RequiredFact(productionVersionFacts, "MbomVersionId", "productEngineering.ProductionVersionCreated"));
        Assert.Equal(
            EngineeringPlanningAcceptanceData.RoutingVersionId,
            RequiredFact(productionVersionFacts, "RoutingVersionId", "productEngineering.ProductionVersionCreated"));
        Assert.Equal(
            RequiredFact(productionVersionFacts, "ProductionVersionId", "productEngineering.ProductionVersionCreated"),
            RequiredFact(workOrderFacts, "ProductionVersionId", "mes.WorkOrderCreated"));
        Assert.Equal(
            RequiredFact(productionVersionFacts, "SkuCode", "productEngineering.ProductionVersionCreated"),
            RequiredFact(workOrderFacts, "SkuCode", "mes.WorkOrderCreated"));
    }

    private static BusinessChainAcceptanceSurface FindChain(string chainName)
    {
        return Assert.Single(BusinessFullChainAcceptanceSurface.Chains, chain => chain.ChainName == chainName);
    }

    private static EndpointSurface Endpoint(string service, string method, string route, string operationId)
    {
        return new EndpointSurface(service, method, route, operationId);
    }

    private static BusinessAcceptanceRecordedEvent SingleEvent(
        IEnumerable<BusinessAcceptanceRecordedEvent> events,
        string service,
        string eventType)
    {
        return Assert.Single(events, x => x.Service == service && x.EventType == eventType);
    }

    private static string RequiredFact(
        IReadOnlyDictionary<string, string> facts,
        string key,
        string eventType)
    {
        Assert.True(
            facts.TryGetValue(key, out var value),
            $"Acceptance event '{eventType}' must expose visible fact '{key}'.");

        return value!;
    }
}
