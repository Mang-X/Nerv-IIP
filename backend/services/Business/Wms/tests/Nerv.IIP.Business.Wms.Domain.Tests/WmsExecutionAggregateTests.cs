using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WmsExecutionAggregateTests
{
    [Fact]
    public void Inbound_completion_requires_idempotency_key_and_creates_inventory_request()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        inbound.CreatePutawayTask("TASK-IN-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m);

        var exception = Assert.Throws<ArgumentException>(() => inbound.Complete(" "));
        var request = Assert.Single(inbound.Complete("idem-in-001"));

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        Assert.Equal("inbound", request.MovementType);
        Assert.Equal("idem-in-001", request.IdempotencyKey);
        Assert.Contains(inbound.GetDomainEvents(), x => x is InboundOrderCompletedDomainEvent);
    }

    [Fact]
    public void Putaway_quantity_cannot_exceed_inbound_line_quantity()
    {
        var inbound = DomainWmsFactory.InboundOrder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            inbound.CreatePutawayTask("TASK-IN-002", "LINE-001", "LOC-STAGE", "LOC-A-01", 6m));

        Assert.Contains("putaway", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Completed_inbound_orders_are_immutable()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        inbound.CreatePutawayTask("TASK-IN-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m);
        inbound.Complete("idem-in-001");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            inbound.CreatePutawayTask("TASK-IN-002", "LINE-001", "LOC-STAGE", "LOC-B-01", 1m));

        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inbound_completion_rejects_empty_persisted_line_set()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        DomainWmsFactory.ClearInboundLines(inbound);

        var exception = Assert.Throws<InvalidOperationException>(() => inbound.Complete("idem-in-001"));

        Assert.Contains("line", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outbound_pack_review_requires_idempotency_key_and_creates_inventory_request()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m);

        var exception = Assert.Throws<ArgumentException>(() => outbound.CompletePackReview("PACK-001", true, " "));
        var request = Assert.Single(outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OutboundOrderStatus.Completed, outbound.Status);
        Assert.Equal("outbound", request.MovementType);
        Assert.Equal("idem-out-001", request.IdempotencyKey);
        Assert.Contains(outbound.GetDomainEvents(), x => x is OutboundOrderCompletedDomainEvent);
    }

    [Fact]
    public void Outbound_pack_review_carries_inventory_reservation_id_to_movement_request()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m, "res-001");

        var request = Assert.Single(outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Equal("res-001", request.InventoryReservationId);
    }

    [Fact]
    public void Outbound_pack_review_rejects_empty_persisted_line_set()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        DomainWmsFactory.ClearOutboundLines(outbound);

        var exception = Assert.Throws<InvalidOperationException>(() => outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Contains("line", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pick_quantity_cannot_exceed_outbound_line_quantity()
    {
        var outbound = DomainWmsFactory.OutboundOrder();

        var exception = Assert.Throws<KnownException>(() =>
            outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 5m));

        Assert.Contains("pick", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Warehouse_task_tracks_execution_bounds()
    {
        var task = WarehouseTask.CreatePutaway(
            "org-001",
            "env-dev",
            "TASK-IN-001",
            "IN-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-STAGE",
            "LOC-A-01",
            5m);

        task.RecordProgress(3m);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => task.RecordProgress(6m));

        Assert.Equal(3m, task.ExecutedQuantity);
        Assert.Contains("executed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DomainWmsFactory
{
    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder InboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "qualified", "company", "owner-001")]);
    }

    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder OutboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
    }

    public static InventoryMovementRequest MovementRequest()
    {
        return InventoryMovementRequest.Create(
            "org-001",
            "env-dev",
            "inbound",
            "IN-001",
            "LINE-001",
            "idem-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            5m);
    }

    public static void ClearInboundLines(Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder inbound)
    {
        var lines = (List<InboundOrderLine>)typeof(Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder)
            .GetField("lines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(inbound)!;
        lines.Clear();
    }

    public static void ClearOutboundLines(Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder outbound)
    {
        var lines = (List<OutboundOrderLine>)typeof(Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder)
            .GetField("lines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(outbound)!;
        lines.Clear();
    }
}
