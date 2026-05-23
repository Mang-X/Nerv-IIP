using System.Text.Json;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEvents;
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
}
