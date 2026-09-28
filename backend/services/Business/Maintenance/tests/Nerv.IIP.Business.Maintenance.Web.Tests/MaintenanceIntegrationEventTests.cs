using System.Text.Json;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceIntegrationEventTests
{
    [Fact]
    public void Asset_unavailable_converter_matches_common_contract_shape()
    {
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        var fromUtc = DateTimeOffset.UtcNow;
        workOrder.MarkAssetUnavailable(fromUtc, "over temperature");

        var integrationEvent = new AssetUnavailableIntegrationEventConverter().Convert(new AssetUnavailableDomainEvent(workOrder, "over temperature", fromUtc));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(MaintenanceIntegrationEventTypes.AssetUnavailable, integrationEvent.EventType);
        Assert.Equal(MaintenanceIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal(MaintenanceIntegrationEventSources.Maintenance, integrationEvent.SourceService);
        Assert.Equal("DEV-CNC-01", integrationEvent.Payload.DeviceAssetId);
        Assert.Contains("\"eventType\":\"maintenance.AssetUnavailable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payload\":{\"deviceAssetId\":\"DEV-CNC-01\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_restored_and_local_work_order_events_use_required_event_types()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "planned maintenance");
        workOrder.Complete("fixed", "minor-stop", 5, []);

        var restored = new AssetRestoredIntegrationEventConverter().Convert(new AssetRestoredDomainEvent(workOrder, workOrder.CompletedAtUtc!.Value));
        var opened = new MaintenanceWorkOrderOpenedIntegrationEventConverter().Convert(new MaintenanceWorkOrderOpenedDomainEvent(workOrder));
        var completed = new MaintenanceWorkOrderCompletedIntegrationEventConverter().Convert(new MaintenanceWorkOrderCompletedDomainEvent(workOrder));

        Assert.Equal(MaintenanceIntegrationEventTypes.AssetRestored, restored.EventType);
        Assert.Equal(MaintenanceLocalIntegrationEventTypes.WorkOrderOpened, opened.EventType);
        Assert.Equal(MaintenanceLocalIntegrationEventTypes.WorkOrderCompleted, completed.EventType);
    }

    [Fact]
    public void Spare_part_issue_converter_requests_inventory_outbound_movement()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        workOrder.Complete(
            "fixed",
            "minor-stop",
            5,
            [new SparePartLineDraft("SPARE-001", 2m, "pcs", "SITE-001", "loc-spare-01")]);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<MaintenanceSparePartIssuedDomainEvent>());

        var integrationEvent = new MaintenanceSparePartIssuedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal(InventoryIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal("maintenance", integrationEvent.Payload.SourceService);
        Assert.Equal("outbound", integrationEvent.Payload.MovementType);
        Assert.Equal(workOrder.Id.ToString(), integrationEvent.Payload.SourceDocumentId);
        Assert.Equal(domainEvent.SparePartLine.Id.ToString(), integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal("SPARE-001", integrationEvent.Payload.SkuCode);
        Assert.Equal("pcs", integrationEvent.Payload.UomCode);
        // #3902：领出工厂与库位来自领用行，不再是占位值；备件是企业自有库存，归属类型是 company。
        Assert.Equal("SITE-001", integrationEvent.Payload.SiteCode);
        Assert.Equal("loc-spare-01", integrationEvent.Payload.LocationCode);
        Assert.Equal("company", integrationEvent.Payload.OwnerType);
        Assert.Equal("available", integrationEvent.Payload.QualityStatus);
        Assert.Equal(-2m, integrationEvent.Payload.Quantity);
        Assert.Contains(workOrder.Id.ToString(), integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression for #1285: the converter used to fall back to a fabricated unit of measure when a spare
    /// part line carried none. That unit does not exist in the unit master data, so the inventory movement
    /// either failed downstream or posted against the wrong quantity. A missing unit is a data defect and
    /// must surface at the source instead.
    /// </summary>
    [Fact]
    public void Spare_part_issue_converter_refuses_to_invent_a_missing_unit_of_measure()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        workOrder.Complete("fixed", "minor-stop", 5, [new SparePartLineDraft("SPARE-001", 2m, SiteCode: "SITE-001", LocationCode: "loc-spare-01")]);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<MaintenanceSparePartIssuedDomainEvent>());

        var error = Assert.Throws<InvalidOperationException>(
            () => new MaintenanceSparePartIssuedIntegrationEventConverter().Convert(domainEvent));

        Assert.Contains("unit of measure", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #3902：领出工厂 / 库位缺失同样是源头数据缺陷。以前写死 maintenance / maintenance-spares，
    /// Inventory 拒收未登记库位后每一笔都会失败；现在缺了就在源头显形，不再发占位值。
    /// </summary>
    [Theory]
    [InlineData(null, "loc-spare-01")]
    [InlineData("SITE-001", null)]
    [InlineData(" ", "loc-spare-01")]
    public void Spare_part_issue_converter_refuses_to_invent_an_issue_location(string? siteCode, string? locationCode)
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        workOrder.Complete("fixed", "minor-stop", 5, [new SparePartLineDraft("SPARE-001", 2m, "pcs", siteCode, locationCode)]);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<MaintenanceSparePartIssuedDomainEvent>());

        var error = Assert.Throws<InvalidOperationException>(
            () => new MaintenanceSparePartIssuedIntegrationEventConverter().Convert(domainEvent));

        Assert.Contains("issue site/location", error.Message, StringComparison.Ordinal);
    }
}
