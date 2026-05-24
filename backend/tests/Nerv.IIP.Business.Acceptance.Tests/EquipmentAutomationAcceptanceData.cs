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
            "listMaintenanceWorkOrders",
            "MES capacity impact is asserted through schedule-run behavior only; there is no dedicated public capacity ledger yet."),
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
            "failWmsWcsTask",
            "Failure diagnostics are command-visible, but there is no dedicated public WCS diagnostics query yet."),
        new(
            "#77 harness baseline: WCS adapter",
            "WcsTaskCompleted",
            "BusinessWms",
            "wms.WcsTaskCompleted",
            "completeWmsWcsTask",
            "Completion is command-visible; the current public event contract lacks a dedicated WCS completed event."),
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
