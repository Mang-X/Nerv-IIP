using System.Text.Json;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEvents;

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
        Assert.Contains("\"eventType\":\"wms.InboundOrderCompleted\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Outbound_count_and_wcs_events_use_required_event_types()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CompletePackReview("PACK-001", true, "idem-out-001");
        var count = CountExecution.Create("org-001", "env-dev", "COUNT-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);
        count.Complete(8m);
        var wcs = WcsTask.Dispatch(new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-001", "{}");
        wcs.Fail("E001", "blocked");

        Assert.Equal(WmsIntegrationEventTypes.OutboundOrderCompleted, new OutboundOrderCompletedIntegrationEventConverter().Convert(new OutboundOrderCompletedDomainEvent(outbound)).EventType);
        Assert.Equal(WmsIntegrationEventTypes.CountExecutionCompleted, new CountExecutionCompletedIntegrationEventConverter().Convert(new CountExecutionCompletedDomainEvent(count)).EventType);
        Assert.Equal(WmsIntegrationEventTypes.WcsTaskDispatched, new WcsTaskDispatchedIntegrationEventConverter().Convert(new WcsTaskDispatchedDomainEvent(wcs)).EventType);
        Assert.Equal(WmsIntegrationEventTypes.WcsTaskFailed, new WcsTaskFailedIntegrationEventConverter().Convert(new WcsTaskFailedDomainEvent(wcs)).EventType);
    }
}
