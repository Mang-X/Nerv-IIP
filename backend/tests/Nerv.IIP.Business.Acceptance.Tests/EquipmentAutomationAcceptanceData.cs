namespace Nerv.IIP.Business.Acceptance.Tests;

public static class EquipmentAutomationAcceptanceData
{
    public static IReadOnlyCollection<VisibleFactMetadata> EquipmentToMaintenanceVisibleFacts { get; } =
    [
        new(
            "#77 harness baseline: Equipment to maintenance",
            "AlarmRaised",
            "BusinessIndustrialTelemetry",
            "industrialTelemetry.AlarmRaised",
            "listBusinessIiotAlarms",
            "Alarm event must be visible through the public alarm list and device timeline."),
        new(
            "#77 harness baseline: Equipment to maintenance",
            "AssetUnavailable",
            "BusinessMaintenance",
            "maintenance.AssetUnavailable",
            "listMaintenanceWorkOrders",
            "Maintenance work order list is the public acceptance surface for the unavailable asset fact."),
        new(
            "#77 harness baseline: Equipment to maintenance",
            "AssetRestored",
            "BusinessMaintenance",
            "maintenance.AssetRestored",
            "listBusinessMesCapacityImpacts",
            "MES capacity impact is visible through the public capacity impact query."),
    ];

    public static IReadOnlyCollection<VisibleFactMetadata> WcsAdapterVisibleFacts { get; } =
    [
        new(
            "#77 harness baseline: WCS adapter",
            "WcsTaskDispatched",
            "BusinessWms",
            "wms.WcsTaskDispatched",
            "dispatchWmsWcsTask",
            "Dispatch fact is visible through the WCS adapter command surface and recorded integration event."),
        new(
            "#77 harness baseline: WCS adapter",
            "WcsTaskFailed",
            "BusinessWms",
            "wms.WcsTaskFailed",
            "listWmsWcsTasks",
            "Failure diagnostics are visible through the public WCS task query."),
        new(
            "#77 harness baseline: WCS adapter",
            "WcsTaskCompleted",
            "BusinessWms",
            "wms.WcsTaskCompleted",
            "listWmsWcsTasks",
            "Completion is visible through the public WCS task query and common WMS event contract."),
        new(
            "#77 harness baseline: WCS adapter",
            "InventoryMovementGateBeforeWarehouseCompletion",
            "BusinessInventory",
            "inventory.StockAvailabilityChanged",
            "getInventoryAvailability",
            "Acceptance must verify stock availability stays unchanged before WMS inbound/outbound completion posts Inventory movement."),
    ];
}

public sealed record VisibleFactMetadata(
    string ChainName,
    string FactName,
    string SourceService,
    string EventType,
    string EvidenceOperationId,
    string RiskNote);
