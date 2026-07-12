namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class EquipmentToMaintenanceAcceptanceTests
{
    private const string ChainName = "#77 harness baseline: Equipment to maintenance";
    private readonly BusinessAcceptanceFixture _fixture;

    public EquipmentToMaintenanceAcceptanceTests(BusinessAcceptanceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Equipment_to_maintenance_surface_exposes_public_fact_queries_for_alarm_downtime_and_capacity()
    {
        var chain = GetChain();

        AssertRequiredEndpoints(chain,
        [
            new EndpointSurface("BusinessMasterData", "POST", "/api/business/v1/master-data/device-assets", "registerBusinessMasterDataDeviceAsset"),
            new EndpointSurface("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/tags", "createBusinessIiotTelemetryTag"),
            new EndpointSurface("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/samples", "recordBusinessIiotTelemetrySample"),
            new EndpointSurface("BusinessIndustrialTelemetry", "POST", "/api/business/v1/iiot/alarms", "raiseBusinessIiotAlarm"),
            new EndpointSurface("BusinessIndustrialTelemetry", "GET", "/api/business/v1/iiot/alarms", "listBusinessIiotAlarms"),
            new EndpointSurface("BusinessIndustrialTelemetry", "GET", "/api/business/v1/iiot/devices/{deviceAssetId}/timeline", "queryBusinessIiotDeviceTimeline"),
            new EndpointSurface("BusinessIndustrialTelemetry", "GET", "/api/business/v1/iiot/runtime-hours", "queryBusinessIiotRuntimeHours"),
            new EndpointSurface("BusinessMaintenance", "POST", "/api/business/v1/maintenance/work-orders", "createMaintenanceWorkOrder"),
            new EndpointSurface("BusinessMaintenance", "GET", "/api/business/v1/maintenance/work-orders", "listMaintenanceWorkOrders"),
            new EndpointSurface("BusinessMaintenance", "POST", "/api/business/v1/maintenance/work-orders/{workOrderId}/complete", "completeMaintenanceWorkOrder"),
            new EndpointSurface("BusinessMaintenance", "POST", "/api/business/v1/maintenance/plans/generate-due", "generateDueMaintenanceWorkOrders"),
            new EndpointSurface("BusinessMes", "POST", "/api/business/v1/mes/schedules/run", "runBusinessMesSchedule"),
            new EndpointSurface("BusinessMes", "GET", "/api/business/v1/mes/capacity-impacts", "listBusinessMesCapacityImpacts"),
        ]);
    }

    [Fact]
    public void Equipment_to_maintenance_visible_fact_metadata_is_backed_by_public_catalog_entries()
    {
        var catalogOperationIds = PublicBusinessEndpointCatalog.All.Select(x => x.OperationId).ToHashSet(StringComparer.Ordinal);
        var facts = EquipmentAutomationAcceptanceData.EquipmentToMaintenanceVisibleFacts;

        Assert.Collection(
            facts,
            fact => AssertVisibleFact(fact, "AlarmRaised", "industrialTelemetry.AlarmRaised", catalogOperationIds),
            fact => AssertVisibleFact(fact, "AssetUnavailable", "maintenance.AssetUnavailable", catalogOperationIds),
            fact => AssertVisibleFact(fact, "AssetRestored", "maintenance.AssetRestored", catalogOperationIds));
    }

    [Fact]
    public void Equipment_to_maintenance_event_recorder_keeps_alarm_unavailable_and_restored_facts_visible_by_correlation()
    {
        var correlation = _fixture.BeginCorrelation("equipment-to-maintenance");

        _fixture.Events.Record("BusinessIndustrialTelemetry", "industrialTelemetry.AlarmRaised", correlation, new { DeviceAssetId = "asset-001", AlarmId = "alarm-001" });
        _fixture.Events.Record("BusinessMaintenance", "maintenance.AssetUnavailable", correlation, new { DeviceAssetId = "asset-001", WorkOrderId = "mw-001" });
        _fixture.Events.Record("BusinessMaintenance", "maintenance.AssetRestored", correlation, new { DeviceAssetId = "asset-001", WorkOrderId = "mw-001" });

        var eventTypes = _fixture.Events.ForCorrelation(correlation.CorrelationId).Select(x => x.EventType).ToArray();

        Assert.Equal(
            ["industrialTelemetry.AlarmRaised", "maintenance.AssetUnavailable", "maintenance.AssetRestored"],
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
