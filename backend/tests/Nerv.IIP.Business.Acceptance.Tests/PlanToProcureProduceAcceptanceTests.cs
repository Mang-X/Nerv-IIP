namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class PlanToProcureProduceAcceptanceTests
{
    private readonly BusinessAcceptanceFixture _fixture;

    public PlanToProcureProduceAcceptanceTests(BusinessAcceptanceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Plan_to_procure_produce_surface_includes_demand_mrp_suggestion_erp_and_mes_public_endpoints()
    {
        var chain = FindChain("#77 harness baseline: Plan to procure/produce");
        var requiredEndpoints = new[]
        {
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/demands", "createOrUpdatePlanningDemand"),
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/mrp-runs", "runPlanningMrp"),
            Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/mrp-runs", "listPlanningMrpRuns"),
            Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/mrp-runs/{runId}/pegging", "getPlanningMrpPegging"),
            Endpoint("BusinessDemandPlanning", "GET", "/api/business/v1/planning/suggestions", "listPlanningSuggestions"),
            Endpoint("BusinessDemandPlanning", "POST", "/api/business/v1/planning/suggestions/{suggestionId}/accept", "acceptPlanningSuggestion"),
            Endpoint("BusinessErp", "POST", "/api/business/v1/erp/purchase-requisitions/from-suggestion", "createErpPurchaseRequisitionFromSuggestion"),
            Endpoint("BusinessMes", "POST", "/api/business/v1/mes/work-orders/rush", "createBusinessMesRushWorkOrder"),
            Endpoint("BusinessMes", "GET", "/api/business/v1/mes/work-orders", "listBusinessMesWorkOrders"),
        };

        Assert.All(requiredEndpoints, endpoint => Assert.Contains(endpoint, chain.RequiredEndpoints));
        Assert.All(requiredEndpoints, endpoint => Assert.Contains(endpoint, PublicBusinessEndpointCatalog.All));
    }

    [Fact]
    public void Plan_to_procure_produce_visible_facts_join_suggestions_to_purchase_requisition_and_mes_work_order()
    {
        var correlation = _fixture.BeginCorrelation("plan-to-procure-produce");

        _fixture.Events.Record("BusinessDemandPlanning", "demandPlanning.MrpRunCompleted", correlation, new
        {
            EngineeringPlanningAcceptanceData.DemandSourceReference,
            EngineeringPlanningAcceptanceData.SkuCode,
        });
        _fixture.Events.Record("BusinessDemandPlanning", "demandPlanning.PlannedPurchaseSuggested", correlation, new
        {
            SuggestionId = EngineeringPlanningAcceptanceData.PlannedPurchaseSuggestionId,
            EngineeringPlanningAcceptanceData.DemandSourceReference,
            EngineeringPlanningAcceptanceData.SkuCode,
        });
        _fixture.Events.Record("BusinessDemandPlanning", "demandPlanning.PlannedWorkOrderSuggested", correlation, new
        {
            SuggestionId = EngineeringPlanningAcceptanceData.PlannedWorkOrderSuggestionId,
            EngineeringPlanningAcceptanceData.DemandSourceReference,
            EngineeringPlanningAcceptanceData.SkuCode,
            EngineeringPlanningAcceptanceData.ProductionVersionId,
        });
        _fixture.Events.Record("BusinessDemandPlanning", "demandPlanning.PlanningSuggestionAccepted", correlation, new
        {
            SuggestionId = EngineeringPlanningAcceptanceData.PlannedPurchaseSuggestionId,
            DownstreamService = "BusinessErp",
            DownstreamDocumentType = "PurchaseRequisition",
            DownstreamDocumentId = (string?)null,
        });
        _fixture.Events.Record("BusinessErp", "erp.PurchaseRequisitionCreated", correlation, new
        {
            EngineeringPlanningAcceptanceData.PurchaseRequisitionNo,
            SuggestionId = EngineeringPlanningAcceptanceData.PlannedPurchaseSuggestionId,
            EngineeringPlanningAcceptanceData.SkuCode,
        });
        _fixture.Events.Record("BusinessMes", "mes.WorkOrderCreated", correlation, new
        {
            EngineeringPlanningAcceptanceData.WorkOrderId,
            SuggestionId = EngineeringPlanningAcceptanceData.PlannedWorkOrderSuggestionId,
            EngineeringPlanningAcceptanceData.SkuCode,
            EngineeringPlanningAcceptanceData.ProductionVersionId,
        });

        var events = _fixture.Events.ForCorrelation(correlation.CorrelationId);
        var accepted = SingleEvent(events, "BusinessDemandPlanning", "demandPlanning.PlanningSuggestionAccepted");
        var requisition = SingleEvent(events, "BusinessErp", "erp.PurchaseRequisitionCreated");
        var workOrderSuggestion = SingleEvent(events, "BusinessDemandPlanning", "demandPlanning.PlannedWorkOrderSuggested");
        var workOrder = SingleEvent(events, "BusinessMes", "mes.WorkOrderCreated");

        var acceptedFacts = EngineeringPlanningAcceptanceData.VisibleFacts(accepted);
        var requisitionFacts = EngineeringPlanningAcceptanceData.VisibleFacts(requisition);
        var workOrderSuggestionFacts = EngineeringPlanningAcceptanceData.VisibleFacts(workOrderSuggestion);
        var workOrderFacts = EngineeringPlanningAcceptanceData.VisibleFacts(workOrder);

        Assert.Equal(
            AcceptanceAssert.RequiredFact(acceptedFacts, "SuggestionId", "demandPlanning.PlanningSuggestionAccepted"),
            AcceptanceAssert.RequiredFact(requisitionFacts, "SuggestionId", "erp.PurchaseRequisitionCreated"));
        Assert.Equal(
            AcceptanceAssert.RequiredFact(workOrderSuggestionFacts, "SuggestionId", "demandPlanning.PlannedWorkOrderSuggested"),
            AcceptanceAssert.RequiredFact(workOrderFacts, "SuggestionId", "mes.WorkOrderCreated"));
        Assert.Equal(
            AcceptanceAssert.RequiredFact(workOrderSuggestionFacts, "ProductionVersionId", "demandPlanning.PlannedWorkOrderSuggested"),
            AcceptanceAssert.RequiredFact(workOrderFacts, "ProductionVersionId", "mes.WorkOrderCreated"));
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
}
