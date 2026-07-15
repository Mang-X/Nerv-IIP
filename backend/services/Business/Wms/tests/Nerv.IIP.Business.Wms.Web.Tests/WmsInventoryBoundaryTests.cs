using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsInventoryBoundaryTests
{
    [Fact]
    public async Task Complete_inbound_atomically_captures_authoritative_line_batch_values()
    {
        await using var dbContext = CreateContext();
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-CAPTURE-001",
            "purchase-receipt",
            "PO-CAPTURE-001",
            "SITE-01",
            [
                new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-OLD", null, "qualified", "company", "owner-001", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                new InboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 3m, "LOC-A-02", "LOT-CLEAR", null, "qualified", "company", "owner-001", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))
            ]);
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(
                inbound.Id,
                "idem-capture-001",
                [
                    new InboundOrderLineCapture(" LINE-001 ", " LOT-NEW ", new DateOnly(2026, 2, 1), new DateOnly(2026, 11, 30)),
                    new InboundOrderLineCapture("LINE-002", null, null, null)
                ]),
            CancellationToken.None);

        var capturedLines = inbound.Lines.OrderBy(x => x.LineNo).ToArray();
        Assert.Equal("LOT-NEW", capturedLines[0].LotNo);
        Assert.Equal(new DateOnly(2026, 2, 1), capturedLines[0].ProductionDate);
        Assert.Equal(new DateOnly(2026, 11, 30), capturedLines[0].ExpiryDate);
        Assert.Null(capturedLines[1].LotNo);
        Assert.Null(capturedLines[1].ProductionDate);
        Assert.Null(capturedLines[1].ExpiryDate);

        var movementRequests = dbContext.InventoryMovementRequests.Local.OrderBy(x => x.SourceDocumentLineId).ToArray();
        Assert.Equal("LOT-NEW", movementRequests[0].LotNo);
        Assert.Equal(new DateOnly(2026, 2, 1), movementRequests[0].ProductionDate);
        Assert.Equal(new DateOnly(2026, 11, 30), movementRequests[0].ExpiryDate);
        Assert.Null(movementRequests[1].LotNo);
        Assert.Null(movementRequests[1].ProductionDate);
        Assert.Null(movementRequests[1].ExpiryDate);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("reversed-dates")]
    public void Complete_inbound_rejects_invalid_captures_before_mutating_any_line(string invalidCase)
    {
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            $"IN-INVALID-{invalidCase}",
            "purchase-receipt",
            $"PO-INVALID-{invalidCase}",
            "SITE-01",
            [
                new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-OLD-1", null, "qualified", "company", "owner-001", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                new InboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 3m, "LOC-A-02", "LOT-OLD-2", null, "qualified", "company", "owner-001", new DateOnly(2026, 2, 1), new DateOnly(2026, 11, 30))
            ]);
        IReadOnlyCollection<InboundOrderLineCapture> captures = invalidCase switch
        {
            "duplicate" =>
            [
                new InboundOrderLineCapture("LINE-001", "LOT-NEW", null, null),
                new InboundOrderLineCapture(" LINE-001 ", "LOT-DUPLICATE", null, null)
            ],
            "unknown" =>
            [
                new InboundOrderLineCapture("LINE-001", "LOT-NEW", null, null),
                new InboundOrderLineCapture("LINE-UNKNOWN", "LOT-UNKNOWN", null, null)
            ],
            "reversed-dates" =>
            [
                new InboundOrderLineCapture("LINE-001", "LOT-NEW", null, null),
                new InboundOrderLineCapture("LINE-002", "LOT-INVALID", new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1))
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null),
        };

        Assert.Throws<InvalidOperationException>(() => inbound.Complete("idem-invalid-001", captures));

        Assert.Equal(InboundOrderStatus.Open, inbound.Status);
        var unchangedLines = inbound.Lines.OrderBy(x => x.LineNo).ToArray();
        Assert.Equal(new string?[] { "LOT-OLD-1", "LOT-OLD-2" }, unchangedLines.Select(x => x.LotNo).ToArray());
        Assert.Equal(new DateOnly?[] { new(2026, 1, 1), new(2026, 2, 1) }, unchangedLines.Select(x => x.ProductionDate).ToArray());
        Assert.Equal(new DateOnly?[] { new(2026, 12, 31), new(2026, 11, 30) }, unchangedLines.Select(x => x.ExpiryDate).ToArray());
    }

    [Fact]
    public void Complete_inbound_rejects_capture_attempts_after_order_is_closed()
    {
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-CLOSED-CAPTURE-001",
            "purchase-receipt",
            "PO-CLOSED-CAPTURE-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-OLD", null, "qualified", "company", "owner-001")]);
        inbound.Complete("idem-close-001");

        Assert.Throws<InvalidOperationException>(() => inbound.Complete(
            "idem-close-002",
            [new InboundOrderLineCapture("LINE-001", "LOT-NEW", null, null)]));

        Assert.Equal("LOT-OLD", inbound.Lines.Single().LotNo);
    }

    [Fact]
    public async Task Complete_inbound_creates_pending_inventory_movement_request_without_http_dependency()
    {
        await using var dbContext = CreateContext();
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-001"),
            CancellationToken.None);

        Assert.Null(result.InventoryMovementId);
        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(result.RequestId, movementRequest.Id);
        Assert.Equal(InventoryMovementRequestStatus.Pending, movementRequest.Status);
        Assert.Equal("inbound", movementRequest.MovementType);
        Assert.Equal("idem-in-001", movementRequest.IdempotencyKey);
        Assert.DoesNotContain(':', movementRequest.IdempotencyKey);
        Assert.Equal("SKU-FG-1000", movementRequest.SkuCode);
    }

    [Fact]
    public async Task Complete_inbound_creates_pending_inventory_movement_request_for_each_line()
    {
        await using var dbContext = CreateContext();
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [
                new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001"),
                new InboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 3m, "LOC-A-02", "LOT-002", null, "qualified", "company", "owner-001")
            ]);
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-001"),
            CancellationToken.None);

        Assert.Null(result.InventoryMovementId);
        var movementRequests = dbContext.InventoryMovementRequests.Local
            .OrderBy(x => x.SourceDocumentLineId)
            .ToArray();
        Assert.Equal(2, movementRequests.Length);
        Assert.Equal(new string?[] { "LINE-001", "LINE-002" }, movementRequests.Select(x => x.SourceDocumentLineId).ToArray());
        Assert.Equal(["SKU-FG-1000", "SKU-RM-2000"], movementRequests.Select(x => x.SkuCode).ToArray());
        Assert.Equal([5m, 3m], movementRequests.Select(x => x.Quantity).ToArray());
        Assert.Equal(movementRequests[0].Id, result.RequestId);
        Assert.All(movementRequests, x => Assert.StartsWith("idem-in-001:", x.IdempotencyKey, StringComparison.Ordinal));
        Assert.Equal(2, movementRequests.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Complete_inbound_keeps_business_completion_pending_when_inventory_is_unavailable()
    {
        await using var dbContext = CreateContext();
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-001"),
            CancellationToken.None);

        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(result.RequestId, movementRequest.Id);
        Assert.Null(result.InventoryMovementId);
        Assert.Equal(InventoryMovementRequestStatus.Pending, movementRequest.Status);
        Assert.Null(movementRequest.FailureCode);
        Assert.Null(movementRequest.FailureMessage);
    }

    [Fact]
    public async Task Complete_outbound_creates_pending_inventory_movement_request()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);

        Assert.Null(result.InventoryMovementId);
        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(result.RequestId, movementRequest.Id);
        Assert.Equal(InventoryMovementRequestStatus.Pending, movementRequest.Status);
        Assert.Equal("outbound", movementRequest.MovementType);
        Assert.Equal("idem-out-001", movementRequest.IdempotencyKey);
        Assert.DoesNotContain(':', movementRequest.IdempotencyKey);
        Assert.Equal(4m, movementRequest.Quantity);
    }

    [Fact]
    public async Task Complete_outbound_creates_pending_inventory_movement_request_for_each_line()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [
                new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001"),
                new OutboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 2m, "LOC-A-02", "LOT-002", null, "qualified", "company", "owner-001")
            ]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);

        Assert.Null(result.InventoryMovementId);
        var movementRequests = dbContext.InventoryMovementRequests.Local
            .OrderBy(x => x.SourceDocumentLineId)
            .ToArray();
        Assert.Equal(2, movementRequests.Length);
        Assert.Equal(new string?[] { "LINE-001", "LINE-002" }, movementRequests.Select(x => x.SourceDocumentLineId).ToArray());
        Assert.Equal(["SKU-FG-1000", "SKU-RM-2000"], movementRequests.Select(x => x.SkuCode).ToArray());
        Assert.Equal([4m, 2m], movementRequests.Select(x => x.Quantity).ToArray());
        Assert.Equal(movementRequests[0].Id, result.RequestId);
        Assert.All(movementRequests, x => Assert.StartsWith("idem-out-001:", x.IdempotencyKey, StringComparison.Ordinal));
        Assert.Equal(2, movementRequests.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Picking_task_reserves_inventory_and_outbound_completion_carries_reservation_id()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");

        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None);
        var result = await new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);

        Assert.Single(inventory.Requests);
        var reserveRequest = inventory.Requests.Single();
        Assert.Equal("OUT-001", reserveRequest.SourceDocumentId);
        Assert.Equal("LINE-001", reserveRequest.SourceDocumentLineId);
        Assert.Equal("LOC-A-01", reserveRequest.LocationCode);
        Assert.Equal(4m, reserveRequest.Quantity);
        Assert.DoesNotContain("TASK-OUT-001", reserveRequest.IdempotencyKey, StringComparison.Ordinal);
        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(result.RequestId, movementRequest.Id);
        Assert.Equal("res-001", movementRequest.InventoryReservationId);
    }

    [Fact]
    public async Task Picking_task_keeps_reservation_and_posting_location_on_the_actual_pick_dimension()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-LOCATION-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-PLANNED", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-location");

        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-LOCATION-001", "LINE-001", "LOC-ACTUAL", "PACK-01", 4m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CompleteWarehouseTaskCommandHandler(dbContext).Handle(
            new CompleteWarehouseTaskCommand(dbContext.WarehouseTasks.Single().Id),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CompleteOutboundOrderCommandHandler(dbContext, inventory).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-LOCATION-001", true, "idem-location-001"),
            CancellationToken.None);

        Assert.Equal("LOC-ACTUAL", Assert.Single(inventory.Requests).LocationCode);
        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal("LOC-ACTUAL", movementRequest.LocationCode);
        Assert.Equal("res-location", movementRequest.InventoryReservationId);
    }

    [Fact]
    public async Task Complete_outbound_posts_executed_pick_quantity_and_releases_short_reservation_balance()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-SHORT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 10m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-short");
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-SHORT-001", "LINE-001", "LOC-A-01", "PACK-01", 10m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var warehouseTask = dbContext.WarehouseTasks.Single();
        await new RecordWarehouseTaskProgressCommandHandler(dbContext).Handle(
            new RecordWarehouseTaskProgressCommand(warehouseTask.Id, 8m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CompleteOutboundOrderCommandHandler(dbContext, inventory).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-SHORT-001", true, "idem-short-001"),
            CancellationToken.None);

        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(8m, movementRequest.Quantity);
        Assert.Equal("res-short", movementRequest.InventoryReservationId);
        var line = outbound.Lines.Single();
        Assert.Equal(8m, line.IssuedQuantity);
        Assert.Equal(2m, line.BackorderQuantity);
        var release = Assert.Single(inventory.ReleaseRequests);
        Assert.Equal("res-short", release.ReservationId);
        Assert.Equal(2m, release.Quantity);
    }

    [Fact]
    public async Task Complete_outbound_requires_inventory_client_before_short_pick_mutates_order()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-SHORT-NO-CLIENT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 10m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-short-no-client");
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-SHORT-NO-CLIENT-001", "LINE-001", "LOC-A-01", "PACK-01", 10m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordWarehouseTaskProgressCommandHandler(dbContext).Handle(
            new RecordWarehouseTaskProgressCommand(dbContext.WarehouseTasks.Single().Id, 8m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() => new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-SHORT-NO-CLIENT-001", true, "idem-short-no-client-001"),
            CancellationToken.None));

        Assert.Equal(OutboundOrderStatus.Open, outbound.Status);
        var line = outbound.Lines.Single();
        Assert.Equal(0m, line.IssuedQuantity);
        Assert.Equal(0m, line.BackorderQuantity);
    }

    [Fact]
    public async Task Cancel_outbound_order_releases_inventory_reservation_and_cancels_open_picking_tasks()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var warehouseTask = Assert.Single(dbContext.WarehouseTasks.Local);
        await new DispatchWcsTaskCommandHandler(dbContext).Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "WCS-OUT-001", """{"step":1}"""),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CancelOutboundOrderCommandHandler(dbContext, inventory).Handle(
            new CancelOutboundOrderCommand(outbound.Id, "customer-cancelled"),
            CancellationToken.None);

        Assert.Equal("Cancelled", outbound.Status.ToString());
        Assert.Null(outbound.Lines.Single().InventoryReservationId);
        var task = Assert.Single(dbContext.WarehouseTasks.Local);
        Assert.Equal("Cancelled", task.Status.ToString());
        var wcsTask = Assert.Single(dbContext.WcsTasks.Local);
        Assert.Equal(WcsTaskStatus.Cancelled, wcsTask.Status);
        var release = Assert.Single(inventory.ReleaseRequests);
        Assert.Equal("res-001", release.ReservationId);
        Assert.Equal(4m, release.Quantity);
    }

    [Fact]
    public async Task Inventory_posting_failed_releases_outbound_reservation_and_keeps_order_retryable()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        var inventory = new FakeWmsInventoryReservationClient("res-001", "res-002");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None);
        await new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MarkInventoryMovementRequestFailedCommandHandler(dbContext, inventory).Handle(
            new MarkInventoryMovementRequestFailedCommand("org-001", "env-dev", "outbound", "OUT-001", "LINE-001", "idem-out-001", "NEGATIVE_ON_HAND", "negative stock"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RetryOutboundInventoryPostingCommandHandler(dbContext, inventory).Handle(
            new RetryOutboundInventoryPostingCommand(outbound.Id, "idem-out-retry-001"),
            CancellationToken.None);

        Assert.Equal("Completed", outbound.Status.ToString());
        Assert.Equal("res-002", outbound.Lines.Single().InventoryReservationId);
        Assert.Collection(
            dbContext.InventoryMovementRequests.Local.OrderBy(x => x.CreatedAtUtc),
            failed => Assert.Equal(InventoryMovementRequestStatus.Failed, failed.Status),
            retried =>
            {
                Assert.Equal(InventoryMovementRequestStatus.Pending, retried.Status);
                Assert.Equal("idem-out-retry-001", retried.IdempotencyKey);
                Assert.Equal("res-002", retried.InventoryReservationId);
            });
        Assert.Equal("res-001", Assert.Single(inventory.ReleaseRequests).ReservationId);
        Assert.Equal(["res-001", "res-002"], inventory.ReservationResults);
    }

    [Fact]
    public async Task Retry_outbound_inventory_posting_only_retries_failed_lines_after_partial_failure()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [
                new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001"),
                new OutboundOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 2m, "LOC-A-02", "LOT-002", null, "qualified", "company", "owner-001")
            ]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-line-1", "res-line-2", "res-line-2-retry");
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None);
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-002", "LINE-002", "LOC-A-02", "PACK-01", 2m),
            CancellationToken.None);
        await new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MarkInventoryMovementRequestPostedCommandHandler(dbContext).Handle(
            new MarkInventoryMovementRequestPostedCommand("org-001", "env-dev", "outbound", "OUT-001", "LINE-001", "idem-out-001:LINE-001", "move-001"),
            CancellationToken.None);
        await new MarkInventoryMovementRequestFailedCommandHandler(dbContext, inventory).Handle(
            new MarkInventoryMovementRequestFailedCommand("org-001", "env-dev", "outbound", "OUT-001", "LINE-002", "idem-out-001:LINE-002", "NEGATIVE_ON_HAND", "negative stock"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new RetryOutboundInventoryPostingCommandHandler(dbContext, inventory).Handle(
            new RetryOutboundInventoryPostingCommand(outbound.Id, "idem-out-retry-001"),
            CancellationToken.None);

        Assert.Equal("res-line-1", outbound.Lines.Single(x => x.LineNo == "LINE-001").InventoryReservationId);
        Assert.Equal("res-line-2-retry", outbound.Lines.Single(x => x.LineNo == "LINE-002").InventoryReservationId);
        Assert.Equal("res-line-2", Assert.Single(inventory.ReleaseRequests).ReservationId);
        Assert.Equal(["res-line-1", "res-line-2", "res-line-2-retry"], inventory.ReservationResults);
        var retryRequest = dbContext.InventoryMovementRequests.Local.Single(x => x.Id == result.RequestId);
        Assert.Equal("LINE-002", retryRequest.SourceDocumentLineId);
        Assert.Equal("idem-out-retry-001", retryRequest.IdempotencyKey);
        Assert.Equal("res-line-2-retry", retryRequest.InventoryReservationId);
        Assert.Equal(3, dbContext.InventoryMovementRequests.Local.Count);
    }

    [Fact]
    public async Task Cancel_outbound_order_validates_state_before_releasing_inventory_reservation()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");
        await new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None);
        await new CompleteOutboundOrderCommandHandler(dbContext).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CancelOutboundOrderCommandHandler(dbContext, inventory).Handle(
            new CancelOutboundOrderCommand(outbound.Id, "late-cancel"),
            CancellationToken.None));

        Assert.Empty(inventory.ReleaseRequests);
    }

    [Fact]
    public async Task Retry_outbound_inventory_posting_validates_state_before_reserving_inventory()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RetryOutboundInventoryPostingCommandHandler(dbContext, inventory).Handle(
            new RetryOutboundInventoryPostingCommand(outbound.Id, "idem-out-retry-001"),
            CancellationToken.None));

        Assert.Empty(inventory.Requests);
    }

    [Fact]
    public async Task Picking_task_does_not_reserve_inventory_when_wms_validation_fails()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");

        await Assert.ThrowsAsync<KnownException>(() => new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 5m),
            CancellationToken.None));

        Assert.Empty(inventory.Requests);
    }

    [Fact]
    public async Task Picking_task_releases_fefo_allocations_when_wms_rejects_split_pick()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-FEFO-SPLIT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", null, null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-fefo-a", "res-fefo-b")
        {
            SplitFefoAllocation = true,
        };

        await Assert.ThrowsAsync<KnownException>(() => new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-FEFO-SPLIT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None));

        Assert.Empty(dbContext.WarehouseTasks.Local);
        Assert.Equal(["res-fefo-a", "res-fefo-b"], inventory.ReleaseRequests.Select(x => x.ReservationId).ToArray());
        Assert.Equal([2m, 2m], inventory.ReleaseRequests.Select(x => x.Quantity).ToArray());
    }

    [Fact]
    public async Task Picking_task_does_not_reserve_inventory_when_outbound_is_closed()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        outbound.CompletePackReview("PACK-001", true, "idem-out-001");
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");

        await Assert.ThrowsAsync<KnownException>(() => new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None));

        Assert.Empty(inventory.Requests);
    }

    [Fact]
    public async Task Picking_task_does_not_reserve_inventory_when_outbound_line_is_missing()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventory = new FakeWmsInventoryReservationClient("res-001");

        await Assert.ThrowsAsync<KnownException>(() => new CreatePickingTaskCommandHandler(dbContext, inventory).Handle(
            new CreatePickingTaskCommand(outbound.Id, "TASK-OUT-001", "LINE-MISSING", "LOC-A-01", "PACK-01", 4m),
            CancellationToken.None));

        Assert.Empty(inventory.Requests);
    }

    [Fact]
    public async Task Reservation_client_preserves_inventory_business_rejection_message()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            """{"data":null,"success":false,"message":"Reservation quantity exceeds available stock.","code":400}"""))
        {
            BaseAddress = new Uri("http://inventory.test"),
        };
        var client = new HttpWmsInventoryReservationClient(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => client.ReserveAsync(
            new WmsInventoryReservationRequest(
                "org-001",
                "env-dev",
                "wms",
                "OUT-001",
                "LINE-001",
                "idem-001",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                null,
                null,
                "qualified",
                "company",
                "owner-001",
                4m),
            CancellationToken.None));

        Assert.Contains("exceeds available stock", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Complete_count_execution_creates_pending_count_adjustment_request()
    {
        await using var dbContext = CreateContext();
        var count = CountExecution.Create("org-001", "env-dev", "COUNT-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);
        dbContext.CountExecutions.Add(count);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new CompleteCountExecutionCommandHandler(dbContext).Handle(
            new CompleteCountExecutionCommand(count.Id, 7.5m, "idem-count-001"),
            CancellationToken.None);

        Assert.Null(result.InventoryMovementId);
        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(result.RequestId, movementRequest.Id);
        Assert.Equal(InventoryMovementRequestStatus.Pending, movementRequest.Status);
        Assert.Equal("count-adjustment", movementRequest.MovementType);
        Assert.Equal(-2.5m, movementRequest.Quantity);
        Assert.Equal("idem-count-001", movementRequest.IdempotencyKey);
    }

    [Fact]
    public async Task Complete_count_execution_confirms_inventory_count_task_without_external_count_adjustment_request()
    {
        await using var dbContext = CreateContext();
        var inventory = new FakeWmsInventoryReservationClient("res-unused")
        {
            CountTaskId = "11111111-1111-7111-8111-111111111111",
            InventoryMovementId = "22222222-2222-7222-8222-222222222222",
        };

        var countId = await new CreateCountExecutionCommandHandler(dbContext, inventory).Handle(
            new CreateCountExecutionCommand("org-001", "env-dev", "COUNT-FREEZE-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var result = await new CompleteCountExecutionCommandHandler(dbContext, inventory).Handle(
            new CompleteCountExecutionCommand(countId, 7.5m, "idem-count-freeze-001"),
            CancellationToken.None);

        Assert.Empty(dbContext.InventoryMovementRequests.Local);
        Assert.Equal("22222222-2222-7222-8222-222222222222", result.InventoryMovementId);
        Assert.Collection(inventory.CountTaskRequests, request =>
        {
            Assert.Equal("COUNT-FREEZE-001", request.CountTaskCode);
            Assert.Equal("LOC-A-01", request.LocationCode);
            Assert.StartsWith("wms-count-freeze:", request.IdempotencyKey, StringComparison.Ordinal);
        });
        Assert.Collection(inventory.CountAdjustmentRequests, request =>
        {
            Assert.Equal("11111111-1111-7111-8111-111111111111", request.CountTaskId);
            Assert.Equal(7.5m, request.CountedQuantity);
            Assert.Equal("idem-count-freeze-001", request.IdempotencyKey);
        });
    }

    [Fact]
    public async Task Complete_wcs_task_records_actual_progress_on_linked_warehouse_task()
    {
        await using var dbContext = CreateContext();
        var warehouseTask = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-WCS-001",
            "OUT-WCS-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            10m);
        dbContext.WarehouseTasks.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(dbContext).Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "WCS-ACTUAL-001", """{"step":1}"""),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CompleteWcsTaskCommandHandler(dbContext).Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "WCS-ACTUAL-001", """{"actualQuantity":8}"""),
            CancellationToken.None);

        Assert.Equal(8m, warehouseTask.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.Open, warehouseTask.Status);
    }

    [Fact]
    public async Task Complete_wcs_task_is_idempotent_when_callback_is_repeated()
    {
        await using var dbContext = CreateContext();
        var warehouseTask = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-WCS-IDEM-001",
            "OUT-WCS-IDEM-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            10m);
        dbContext.WarehouseTasks.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(dbContext).Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "WCS-IDEM-001", """{"step":1}"""),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteWcsTaskCommandHandler(dbContext);

        await handler.Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "WCS-IDEM-001", """{"actualQuantity":10}"""),
            CancellationToken.None);
        await handler.Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "WCS-IDEM-001", """{"actualQuantity":10}"""),
            CancellationToken.None);

        Assert.Equal(10m, warehouseTask.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.Completed, warehouseTask.Status);
        Assert.Equal(WcsTaskStatus.Completed, dbContext.WcsTasks.Single().Status);
    }

    [Fact]
    public async Task Complete_wcs_task_without_explicit_executed_quantity_does_not_advance_warehouse_progress()
    {
        await using var dbContext = CreateContext();
        var warehouseTask = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-WCS-MISSING-QTY-001",
            "OUT-WCS-MISSING-QTY-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            10m);
        dbContext.WarehouseTasks.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(dbContext).Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "WCS-MISSING-QTY-001", """{"step":1}"""),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CompleteWcsTaskCommandHandler(dbContext).Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "WCS-MISSING-QTY-001", """{"ok":true,"quantity":10}"""),
            CancellationToken.None);

        Assert.Equal(0m, warehouseTask.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.Open, warehouseTask.Status);
        Assert.Equal(WcsTaskStatus.Completed, dbContext.WcsTasks.Single().Status);
    }

    [Fact]
    public async Task Recording_progress_on_an_open_picking_task_renews_its_inventory_reservation()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-RENEW-001",
            "sales-order",
            "SO-RENEW-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        var pickingTask = outbound.CreatePickingTask(
            "TASK-RENEW-001",
            "LINE-001",
            "LOC-A-01",
            "PACK-01",
            5m,
            "reservation-renew-001");
        dbContext.OutboundOrders.Add(outbound);
        dbContext.WarehouseTasks.Add(pickingTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventoryClient = new FakeWmsInventoryReservationClient("reservation-renew-001");

        await new RecordWarehouseTaskProgressCommandHandler(dbContext, inventoryClient).Handle(
            new RecordWarehouseTaskProgressCommand(pickingTask.Id, 1m),
            CancellationToken.None);

        var renewal = Assert.Single(inventoryClient.RenewalRequests);
        Assert.Equal("reservation-renew-001", renewal.ReservationId);
    }

    [Fact]
    public async Task Recording_picking_progress_keeps_the_local_progress_when_inventory_renewal_is_temporarily_unavailable()
    {
        await using var dbContext = CreateContext();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-RENEW-UNAVAILABLE-001",
            "sales-order",
            "SO-RENEW-UNAVAILABLE-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        var pickingTask = outbound.CreatePickingTask(
            "TASK-RENEW-UNAVAILABLE-001",
            "LINE-001",
            "LOC-A-01",
            "PACK-01",
            5m,
            "reservation-renew-unavailable-001");
        dbContext.OutboundOrders.Add(outbound);
        dbContext.WarehouseTasks.Add(pickingTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var inventoryClient = new FakeWmsInventoryReservationClient("reservation-renew-unavailable-001")
        {
            RenewalException = new HttpRequestException("Inventory is temporarily unavailable."),
        };

        await new RecordWarehouseTaskProgressCommandHandler(dbContext, inventoryClient).Handle(
            new RecordWarehouseTaskProgressCommand(pickingTask.Id, 1m),
            CancellationToken.None);

        Assert.Equal(1m, pickingTask.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.Open, pickingTask.Status);
        Assert.Single(inventoryClient.RenewalRequests);
    }

    [Fact]
    public async Task Complete_wcs_task_logs_warning_when_completion_payload_has_no_executed_quantity()
    {
        await using var dbContext = CreateContext();
        var warehouseTask = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-WCS-DIAG-001",
            "OUT-WCS-DIAG-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            10m);
        dbContext.WarehouseTasks.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(dbContext).Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "WCS-DIAG-001", """{"step":1}"""),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var logger = new ListLogger<CompleteWcsTaskCommandHandler>();

        await new CompleteWcsTaskCommandHandler(dbContext, logger).Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "WCS-DIAG-001", """{"ok":true}"""),
            CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("WCS-DIAG-001", entry.Message, StringComparison.Ordinal);
        Assert.Contains("executed quantity", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-boundary-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FakeWmsInventoryReservationClient(params string[] reservationIds) : IWmsInventoryReservationClient
    {
        public List<WmsInventoryReservationRequest> Requests { get; } = [];
        public List<WmsInventoryFefoReservationRequest> FefoRequests { get; } = [];
        public List<WmsInventoryReservationReleaseRequest> ReleaseRequests { get; } = [];
        public List<WmsInventoryReservationRenewalRequest> RenewalRequests { get; } = [];
        public List<WmsInventoryCountTaskRequest> CountTaskRequests { get; } = [];
        public List<WmsInventoryCountAdjustmentRequest> CountAdjustmentRequests { get; } = [];
        public List<string> ReservationResults { get; } = [];
        public bool SplitFefoAllocation { get; init; }
        public string CountTaskId { get; init; } = Guid.CreateVersion7().ToString();
        public string InventoryMovementId { get; init; } = Guid.CreateVersion7().ToString();
        public Exception? RenewalException { get; init; }

        public Task<WmsInventoryReservationResult> ReserveAsync(
            WmsInventoryReservationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var reservationId = reservationIds[Math.Min(Requests.Count - 1, reservationIds.Length - 1)];
            ReservationResults.Add(reservationId);
            return Task.FromResult(new WmsInventoryReservationResult(reservationId, request.Quantity, 0m));
        }

        public Task<WmsInventoryFefoReservationResult> ReserveFefoAsync(
            WmsInventoryFefoReservationRequest request,
            CancellationToken cancellationToken)
        {
            FefoRequests.Add(request);
            var reservationId = reservationIds[Math.Min(FefoRequests.Count - 1, reservationIds.Length - 1)];
            ReservationResults.Add(reservationId);
            if (SplitFefoAllocation)
            {
                return Task.FromResult(new WmsInventoryFefoReservationResult(
                    [
                        new WmsInventoryFefoReservationAllocation(reservationIds[0], request.LocationCode ?? "LOC-A-01", "LOT-FEFO-A", null, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), request.Quantity / 2m, 0m),
                        new WmsInventoryFefoReservationAllocation(reservationIds[1], request.LocationCode ?? "LOC-A-01", "LOT-FEFO-B", null, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20), request.Quantity / 2m, 0m)
                    ],
                    request.Quantity));
            }

            return Task.FromResult(new WmsInventoryFefoReservationResult(
                [new WmsInventoryFefoReservationAllocation(reservationId, request.LocationCode ?? "LOC-A-01", "LOT-FEFO", null, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), request.Quantity, 0m)],
                request.Quantity));
        }

        public Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
            WmsInventoryReservationReleaseRequest request,
            CancellationToken cancellationToken)
        {
            ReleaseRequests.Add(request);
            return Task.FromResult(new WmsInventoryReservationReleaseResult(request.ReservationId, 0m, request.Quantity));
        }

        public Task<WmsInventoryReservationRenewalResult> RenewAsync(
            WmsInventoryReservationRenewalRequest request,
            CancellationToken cancellationToken)
        {
            RenewalRequests.Add(request);
            if (RenewalException is not null)
            {
                throw RenewalException;
            }

            return Task.FromResult(new WmsInventoryReservationRenewalResult(request.ReservationId, DateTime.UtcNow.AddHours(2)));
        }

        public Task<WmsInventoryCountTaskResult> CreateCountTaskAsync(
            WmsInventoryCountTaskRequest request,
            CancellationToken cancellationToken)
        {
            CountTaskRequests.Add(request);
            return Task.FromResult(new WmsInventoryCountTaskResult(CountTaskId, 1L));
        }

        public Task<WmsInventoryCountAdjustmentResult> ConfirmCountAdjustmentAsync(
            WmsInventoryCountAdjustmentRequest request,
            CancellationToken cancellationToken)
        {
            CountAdjustmentRequests.Add(request);
            return Task.FromResult(new WmsInventoryCountAdjustmentResult(InventoryMovementId, request.CountedQuantity - 10m, request.CountedQuantity));
        }
    }

    private sealed class TestInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
