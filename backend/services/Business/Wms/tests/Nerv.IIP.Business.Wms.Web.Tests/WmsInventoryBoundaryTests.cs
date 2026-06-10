using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;

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
        Assert.Equal("SKU-FG-1000", movementRequest.SkuCode);
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
        Assert.Equal(4m, movementRequest.Quantity);
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
}
