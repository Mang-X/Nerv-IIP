using System.Text.Json;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsIntegrationEventTests
{
    [Fact]
    public void Inbound_completed_event_uses_required_event_type()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        inbound.Complete("idem-in-001");

        var integrationEvent = new InboundOrderCompletedIntegrationEventConverter().Convert(new InboundOrderCompletedDomainEvent(inbound));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(WmsIntegrationEventTypes.InboundOrderCompleted, integrationEvent.EventType);
        Assert.Equal("Nerv.IIP.Contracts.Wms", typeof(WmsIntegrationEvent).Assembly.GetName().Name);
        Assert.Contains("\"eventType\":\"wms.InboundOrderCompleted\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbound_completed_event_contains_all_completed_lines()
    {
        var inbound = DomainWmsFactory.MultiLineInboundOrder();
        inbound.Complete("idem-in-001");

        var integrationEvent = new InboundOrderCompletedIntegrationEventConverter().Convert(new InboundOrderCompletedDomainEvent(inbound));
        Assert.NotNull(integrationEvent.Payload.Lines);
        var lines = integrationEvent.Payload.Lines!
            .OrderBy(x => x.LineReference, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, lines.Length);
        Assert.Equal(["LINE-001", "LINE-002"], lines.Select(x => x.LineReference).ToArray());
        Assert.Equal(["SKU-FG-1000", "SKU-RM-2000"], lines.Select(x => x.SkuCode).ToArray());
        Assert.Equal([5m, 3m], lines.Select(x => x.Quantity).ToArray());
        Assert.All(lines, x => Assert.Null(x.Status));
    }

    [Fact]
    public void Outbound_completed_event_contains_all_completed_lines()
    {
        var outbound = DomainWmsFactory.MultiLineOutboundOrder();
        outbound.CompletePackReview("PACK-001", true, "idem-out-001");

        var integrationEvent = new OutboundOrderCompletedIntegrationEventConverter().Convert(new OutboundOrderCompletedDomainEvent(outbound));
        Assert.NotNull(integrationEvent.Payload.Lines);
        var lines = integrationEvent.Payload.Lines!
            .OrderBy(x => x.LineReference, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, lines.Length);
        Assert.Equal(["LINE-001", "LINE-002"], lines.Select(x => x.LineReference).ToArray());
        Assert.Equal(["SKU-FG-1000", "SKU-RM-2000"], lines.Select(x => x.SkuCode).ToArray());
        Assert.Equal([4m, 2m], lines.Select(x => x.Quantity).ToArray());
        Assert.All(lines, x => Assert.Null(x.Status));
    }

    [Fact]
    public void Outbound_count_and_wcs_events_use_required_event_types()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CompletePackReview("PACK-001", true, "idem-out-001");
        var cancelledOutbound = DomainWmsFactory.OutboundOrder();
        cancelledOutbound.Cancel("customer-cancelled");
        var count = CountExecution.Create("org-001", "env-dev", "COUNT-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);
        count.Complete(8m);
        var wcs = WcsTask.Dispatch("org-001", "env-dev", new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-001", "{}");
        wcs.Fail("E001", "blocked");

        Assert.Equal(WmsIntegrationEventTypes.OutboundOrderCompleted, new OutboundOrderCompletedIntegrationEventConverter().Convert(new OutboundOrderCompletedDomainEvent(outbound)).EventType);
        var cancelledEvent = new OutboundOrderCancelledIntegrationEventConverter().Convert(new OutboundOrderCancelledDomainEvent(cancelledOutbound));
        Assert.Equal(WmsIntegrationEventTypes.OutboundOrderCancelled, cancelledEvent.EventType);
        Assert.Equal("customer-cancelled", cancelledEvent.Payload.DiagnosticMessage);
        Assert.Equal(WmsIntegrationEventTypes.CountExecutionCompleted, new CountExecutionCompletedIntegrationEventConverter().Convert(new CountExecutionCompletedDomainEvent(count)).EventType);
        Assert.Equal(WmsIntegrationEventTypes.WcsTaskDispatched, new WcsTaskDispatchedIntegrationEventConverter().Convert(new WcsTaskDispatchedDomainEvent(wcs)).EventType);
        Assert.Equal(WmsIntegrationEventTypes.WcsTaskFailed, new WcsTaskFailedIntegrationEventConverter().Convert(new WcsTaskFailedDomainEvent(wcs)).EventType);

        wcs.Retry("EXT-002", "{}");
        wcs.Complete("{}");

        var completedEvent = new WcsTaskCompletedIntegrationEventConverter().Convert(new WcsTaskCompletedDomainEvent(wcs));
        var cancelledWcs = WcsTask.Dispatch("org-001", "env-dev", new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-CANCEL", "{}");
        cancelledWcs.Cancel();
        var cancelledWcsEvent = new WcsTaskCancelledIntegrationEventConverter().Convert(new WcsTaskCancelledDomainEvent(cancelledWcs));

        Assert.Equal(WmsIntegrationEventTypes.WcsTaskCompleted, completedEvent.EventType);
        Assert.Equal(WmsIntegrationEventTypes.WcsTaskCancelled, cancelledWcsEvent.EventType);
        Assert.Equal("org-001", completedEvent.OrganizationId);
        Assert.Equal("env-dev", completedEvent.EnvironmentId);
    }

    [Theory]
    [InlineData("inbound", 5, 5)]
    [InlineData("outbound", 4, -4)]
    [InlineData("count-adjustment", -2.5, -2.5)]
    public void Inventory_movement_requested_event_maps_wms_request_payload_and_signed_quantity(
        string movementType,
        decimal requestQuantity,
        decimal expectedEventQuantity)
    {
        var request = InventoryMovementRequest.Create(
            "org-001",
            "env-dev",
            movementType,
            $"DOC-{movementType}",
            "LINE-001",
            $"idem-{movementType}",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            requestQuantity);

        var integrationEvent = new InventoryMovementRequestCreatedIntegrationEventConverter()
            .Convert(new InventoryMovementRequestCreatedDomainEvent(request));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.False(string.IsNullOrWhiteSpace(integrationEvent.CausationId));
        Assert.Equal(integrationEvent.IdempotencyKey, integrationEvent.CausationId);
        Assert.Equal(InventoryIntegrationEventSources.BusinessWms, integrationEvent.SourceService);
        Assert.Equal("wms", integrationEvent.Payload.SourceService);
        Assert.Equal($"DOC-{movementType}", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal($"idem-{movementType}", integrationEvent.Payload.IdempotencyKey);
        Assert.Equal(expectedEventQuantity, integrationEvent.Payload.Quantity);
    }
}
