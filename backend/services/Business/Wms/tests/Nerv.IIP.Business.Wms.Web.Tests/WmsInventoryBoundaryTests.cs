using System.Net;
using System.Text;
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

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsInventoryBoundaryTests
{
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
        public List<WmsInventoryReservationReleaseRequest> ReleaseRequests { get; } = [];
        public List<string> ReservationResults { get; } = [];

        public Task<WmsInventoryReservationResult> ReserveAsync(
            WmsInventoryReservationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var reservationId = reservationIds[Math.Min(Requests.Count - 1, reservationIds.Length - 1)];
            ReservationResults.Add(reservationId);
            return Task.FromResult(new WmsInventoryReservationResult(reservationId, request.Quantity, 0m));
        }

        public Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
            WmsInventoryReservationReleaseRequest request,
            CancellationToken cancellationToken)
        {
            ReleaseRequests.Add(request);
            return Task.FromResult(new WmsInventoryReservationReleaseResult(request.ReservationId, 0m, request.Quantity));
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
}
