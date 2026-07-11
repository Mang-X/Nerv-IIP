using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Queries;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsShortPickBackorderTests
{
    [Fact]
    public async Task Completing_short_pick_persists_one_backorder_and_one_replenishment_recommendation_on_retry()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var outbound = OutboundOrder.Create(
            "org-001", "env-dev", "OUT-001", "sales-delivery", "SO-001", "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-001", "pcs", 10m, "PICK-01", null, null, "qualified", "company", null)]);
        var picking = outbound.CreatePickingTask("PICK-OUT-001-001", "LINE-001", "PICK-01", "PACK-01", 10m);
        picking.RecordProgress(7m);
        dbContext.AddRange(outbound, picking);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteOutboundOrderCommandHandler(dbContext);
        var command = new CompleteOutboundOrderCommand(outbound.Id, "PACK-001", true, "complete-out-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var backorder = Assert.Single(await dbContext.BackorderOrders.AsNoTracking().ToListAsync());
        Assert.Equal("OUT-001", backorder.OutboundOrderNo);
        Assert.Equal("LINE-001", backorder.OutboundOrderLineNo);
        Assert.Equal(3m, backorder.BackorderQuantity);
        var recommendation = Assert.Single(await dbContext.WarehouseTasks.AsNoTracking()
            .Where(x => x.TaskType == WarehouseTaskType.Replenishment)
            .ToListAsync());
        Assert.Equal(backorder.BackorderOrderNo, recommendation.SourceOrderNo);
        Assert.Equal("PICK-01", recommendation.ToLocationCode);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task Backorder_query_is_tenant_scoped_and_close_is_idempotent()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var backorder = Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrder.Create(
            "org-001", "env-dev", "BO-001", "OUT-001", "LINE-001", "SKU-001", "pcs", "SITE-01", "PICK-01", 3m);
        dbContext.BackorderOrders.Add(backorder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var query = await new ListBackorderOrdersQueryHandler(dbContext).Handle(
            new ListBackorderOrdersQuery("org-001", "env-dev"), CancellationToken.None);
        await new CloseBackorderOrderCommandHandler(dbContext).Handle(
            new CloseBackorderOrderCommand(backorder.Id, "stock-restored"), CancellationToken.None);
        await new CloseBackorderOrderCommandHandler(dbContext).Handle(
            new CloseBackorderOrderCommand(backorder.Id, "stock-restored"), CancellationToken.None);

        Assert.Single(query.Items);
        Assert.Equal("BO-001", query.Items.Single().BackorderOrderNo);
        Assert.Equal(Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrderStatus.Closed, backorder.Status);
    }

    [Fact]
    public void Close_backorder_validator_rejects_blank_and_oversized_reasons()
    {
        var validator = new CloseBackorderOrderCommandValidator();
        var id = new Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrderId(Guid.CreateVersion7());

        Assert.False(validator.Validate(new CloseBackorderOrderCommand(id, " ")).IsValid);
        Assert.False(validator.Validate(new CloseBackorderOrderCommand(id, new string('x', 1001))).IsValid);
        Assert.True(validator.Validate(new CloseBackorderOrderCommand(id, "stock-restored")).IsValid);
    }

    [Fact]
    public async Task Closing_backorder_with_a_conflicting_reason_returns_known_error()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var backorder = Domain.AggregatesModel.BackorderOrderAggregate.BackorderOrder.Create(
            "org-001", "env-dev", "BO-001", "OUT-001", "LINE-001", "SKU-001", "pcs", "SITE-01", "PICK-01", 3m);
        backorder.Close("stock-restored");
        dbContext.BackorderOrders.Add(backorder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CloseBackorderOrderCommandHandler(dbContext);

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() => handler.Handle(
            new CloseBackorderOrderCommand(backorder.Id, "customer-cancelled"), CancellationToken.None));
    }
}
