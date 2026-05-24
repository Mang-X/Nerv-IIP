namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class WcsAdapterAcceptanceTests
{
    private const string ChainName = "#77 harness baseline: WCS adapter";
    private readonly BusinessAcceptanceFixture _fixture;

    public WcsAdapterAcceptanceTests(BusinessAcceptanceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Wcs_adapter_surface_exposes_inbound_outbound_dispatch_diagnostics_completion_and_inventory_gate()
    {
        var chain = GetChain();

        AssertRequiredEndpoints(chain,
        [
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders", "createWmsInboundOrder"),
            new EndpointSurface("BusinessWms", "GET", "/api/business/v1/wms/inbound-orders", "listWmsInboundOrders"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks", "createWmsPutawayTask"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", "completeWmsInboundOrder"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders", "createWmsOutboundOrder"),
            new EndpointSurface("BusinessWms", "GET", "/api/business/v1/wms/outbound-orders", "listWmsOutboundOrders"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", "createWmsPickingTask"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete", "completeWmsOutboundOrder"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", "dispatchWmsWcsTask"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", "failWmsWcsTask"),
            new EndpointSurface("BusinessWms", "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete", "completeWmsWcsTask"),
            new EndpointSurface("BusinessWms", "GET", "/api/business/v1/wms/wcs-tasks", "listWmsWcsTasks"),
            new EndpointSurface("BusinessInventory", "GET", "/api/inventory/v1/availability", "getInventoryAvailability"),
            new EndpointSurface("BusinessInventory", "POST", "/api/inventory/v1/movements", "postInventoryMovement"),
        ]);
    }

    [Fact]
    public void Wcs_adapter_visible_fact_metadata_marks_diagnostics_and_inventory_movement_gate_risks()
    {
        var catalogOperationIds = PublicBusinessEndpointCatalog.All.Select(x => x.OperationId).ToHashSet(StringComparer.Ordinal);
        var facts = EquipmentAutomationAcceptanceData.WcsAdapterVisibleFacts;

        Assert.Collection(
            facts,
            fact => AssertVisibleFact(fact, "WcsTaskDispatched", "wms.WcsTaskDispatched", catalogOperationIds),
            fact => AssertVisibleFact(fact, "WcsTaskFailed", "wms.WcsTaskFailed", catalogOperationIds),
            fact => AssertVisibleFact(fact, "WcsTaskCompleted", "wms.WcsTaskCompleted", catalogOperationIds),
            fact =>
            {
                AssertVisibleFact(fact, "InventoryMovementGateBeforeWarehouseCompletion", "inventory.StockAvailabilityChanged", catalogOperationIds);
                Assert.Contains("before WMS inbound/outbound completion", fact.RiskNote, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Wcs_adapter_event_recorder_keeps_fail_dispatch_and_complete_surface_facts_visible_by_correlation()
    {
        var correlation = _fixture.BeginCorrelation("wms-wcs-adapter");

        _fixture.Events.Record("BusinessWms", "wms.WcsTaskDispatched", correlation, new { ExternalTaskId = "wcs-001", Attempt = 1 });
        _fixture.Events.Record("BusinessWms", "wms.WcsTaskFailed", correlation, new { ExternalTaskId = "wcs-001", FailureCode = "PLC_TIMEOUT" });
        _fixture.Events.Record("BusinessWms", "wms.WcsTaskDispatched", correlation, new { ExternalTaskId = "wcs-001", Attempt = 2 });
        _fixture.Events.Record("BusinessWms", "wms.WcsTaskCompleted", correlation, new { ExternalTaskId = "wcs-001" });
        _fixture.Events.Record("BusinessInventory", "inventory.StockAvailabilityChanged", correlation, new { MovementType = "inbound", PostedAfterWarehouseCompletion = true });

        var eventTypes = _fixture.Events.ForCorrelation(correlation.CorrelationId).Select(x => x.EventType).ToArray();

        Assert.Equal(
            ["wms.WcsTaskDispatched", "wms.WcsTaskFailed", "wms.WcsTaskDispatched", "wms.WcsTaskCompleted", "inventory.StockAvailabilityChanged"],
            eventTypes);
    }

    private static BusinessChainAcceptanceSurface GetChain()
    {
        return Assert.Single(BusinessFullChainAcceptanceSurface.Chains, x => x.ChainName == ChainName);
    }

    private static void AssertRequiredEndpoints(BusinessChainAcceptanceSurface chain, IReadOnlyCollection<EndpointSurface> requiredEndpoints)
    {
        var missing = requiredEndpoints.Where(endpoint => !chain.RequiredEndpoints.Contains(endpoint)).ToArray();

        Assert.Empty(missing);
    }

    private static void AssertVisibleFact(VisibleFactMetadata fact, string factName, string eventType, HashSet<string> catalogOperationIds)
    {
        Assert.Equal(ChainName, fact.ChainName);
        Assert.Equal(factName, fact.FactName);
        Assert.Equal(eventType, fact.EventType);
        Assert.Contains(fact.EvidenceOperationId, catalogOperationIds);
        Assert.False(string.IsNullOrWhiteSpace(fact.RiskNote));
    }
}
