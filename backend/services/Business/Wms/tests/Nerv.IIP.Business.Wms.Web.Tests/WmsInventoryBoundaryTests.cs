using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsInventoryBoundaryTests
{
    [Fact]
    public async Task Complete_inbound_posts_inventory_payload_with_idempotency_key()
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
        var fake = new RecordingInventoryMovementClient();

        var result = await new CompleteInboundOrderCommandHandler(dbContext, fake).Handle(
            new CompleteInboundOrderCommand(inbound.Id, "idem-in-001"),
            CancellationToken.None);

        Assert.Equal("posted-inbound-idem-in-001", result.InventoryMovementId);
        Assert.Single(fake.Requests);
        Assert.Equal("inbound", fake.Requests[0].MovementType);
        Assert.Equal("idem-in-001", fake.Requests[0].IdempotencyKey);
        Assert.Equal("SKU-FG-1000", fake.Requests[0].SkuCode);
    }

    [Fact]
    public async Task Complete_inbound_marks_inventory_movement_request_failed_when_inventory_post_fails()
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
        var failure = new TimeoutException("Inventory posting timed out.");
        var fake = new FailingInventoryMovementClient(failure);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            new CompleteInboundOrderCommandHandler(dbContext, fake).Handle(
                new CompleteInboundOrderCommand(inbound.Id, "idem-in-001"),
                CancellationToken.None));

        Assert.Same(failure, exception);
        var movementRequest = Assert.Single(dbContext.InventoryMovementRequests.Local);
        Assert.Equal(InventoryMovementRequestStatus.Failed, movementRequest.Status);
        Assert.Equal(nameof(TimeoutException), movementRequest.FailureCode);
        Assert.Equal("Inventory posting timed out.", movementRequest.FailureMessage);
    }

    [Fact]
    public async Task Complete_outbound_posts_inventory_payload_with_idempotency_key()
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
        var fake = new RecordingInventoryMovementClient();

        var result = await new CompleteOutboundOrderCommandHandler(dbContext, fake).Handle(
            new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "idem-out-001"),
            CancellationToken.None);

        Assert.Equal("posted-outbound-idem-out-001", result.InventoryMovementId);
        Assert.Single(fake.Requests);
        Assert.Equal("outbound", fake.Requests[0].MovementType);
        Assert.Equal("idem-out-001", fake.Requests[0].IdempotencyKey);
        Assert.Equal(-4m, fake.Requests[0].Quantity);
    }

    [Fact]
    public async Task Complete_count_execution_posts_count_adjustment_payload_with_variance_quantity()
    {
        await using var dbContext = CreateContext();
        var count = CountExecution.Create("org-001", "env-dev", "COUNT-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);
        dbContext.CountExecutions.Add(count);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var fake = new RecordingInventoryMovementClient();

        var result = await new CompleteCountExecutionCommandHandler(dbContext, fake).Handle(
            new CompleteCountExecutionCommand(count.Id, 7.5m, "idem-count-001"),
            CancellationToken.None);

        Assert.Equal("posted-count-adjustment-idem-count-001", result.InventoryMovementId);
        Assert.Single(fake.Requests);
        Assert.Equal("count-adjustment", fake.Requests[0].MovementType);
        Assert.Equal(-2.5m, fake.Requests[0].Quantity);
        Assert.Equal("idem-count-001", fake.Requests[0].IdempotencyKey);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-boundary-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FailingInventoryMovementClient(Exception exception) : IInventoryMovementClient
    {
        public Task<PostInventoryMovementResult> PostMovementAsync(PostInventoryMovementRequest request, CancellationToken cancellationToken)
        {
            return Task.FromException<PostInventoryMovementResult>(exception);
        }
    }
}
