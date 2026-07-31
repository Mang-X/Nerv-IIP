using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// GitHub #1346 第一条：transfer 类型单腿过账实证凭空 +1L 库存。
/// 调拨必须一次提交配平的两腿（出库位减、入库位增、数量等额），否则整笔拒绝。
/// </summary>
public sealed class InventoryTransferBalanceTests
{
    private const string SourceLocation = "LOC-A-01";
    private const string TargetLocation = "LOC-B-01";

    [Fact]
    public async Task Single_leg_transfer_is_rejected_and_leaves_stock_untouched()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await SeedSourceStockAsync(handler, dbContext, 10m);

        var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(
            TransferCommand("idem-transfer-single-leg", quantity: 1m, transferInLocationCode: null, transferInQuantity: null),
            CancellationToken.None));

        Assert.Equal(InventoryPostingFailureCodes.TransferLegsUnbalanced, exception.FailureCode);
        Assert.Contains("必须一次提交两腿", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var ledger = Assert.Single(dbContext.StockLedgers);
        Assert.Equal(SourceLocation, ledger.LocationCode);
        Assert.Equal(10m, ledger.OnHandQuantity);
    }

    [Fact]
    public async Task Transfer_with_unequal_legs_is_rejected_whole()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await SeedSourceStockAsync(handler, dbContext, 10m);

        var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(
            TransferCommand("idem-transfer-unbalanced", quantity: -3m, transferInLocationCode: TargetLocation, transferInQuantity: 5m),
            CancellationToken.None));

        Assert.Equal(InventoryPostingFailureCodes.TransferLegsUnbalanced, exception.FailureCode);
        Assert.Contains("必须配平", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(10m, Assert.Single(dbContext.StockLedgers).OnHandQuantity);
    }

    [Fact]
    public async Task Transfer_into_the_same_location_is_rejected()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await SeedSourceStockAsync(handler, dbContext, 10m);

        var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(
            TransferCommand("idem-transfer-same-location", quantity: -2m, transferInLocationCode: SourceLocation, transferInQuantity: 2m),
            CancellationToken.None));

        Assert.Equal(InventoryPostingFailureCodes.TransferLegsUnbalanced, exception.FailureCode);
        Assert.Contains("不能与出库库位相同", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transfer_out_leg_must_be_negative()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await SeedSourceStockAsync(handler, dbContext, 10m);

        var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(
            TransferCommand("idem-transfer-positive-out", quantity: 2m, transferInLocationCode: TargetLocation, transferInQuantity: 2m),
            CancellationToken.None));

        Assert.Equal(InventoryPostingFailureCodes.TransferLegsUnbalanced, exception.FailureCode);
        Assert.Contains("出库腿数量必须为负数", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Balanced_transfer_moves_stock_and_writes_two_offsetting_ledger_movements()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await SeedSourceStockAsync(handler, dbContext, 10m);

        var result = await handler.Handle(
            TransferCommand("idem-transfer-balanced", quantity: -4m, transferInLocationCode: TargetLocation, transferInQuantity: 4m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.NotNull(result.TransferInMovementId);
        Assert.Equal(6m, result.OnHandQuantity);
        Assert.Equal(4m, result.TransferInOnHandQuantity);

        var source = dbContext.StockLedgers.Single(x => x.LocationCode == SourceLocation);
        var target = dbContext.StockLedgers.Single(x => x.LocationCode == TargetLocation);
        Assert.Equal(6m, source.OnHandQuantity);
        Assert.Equal(4m, target.OnHandQuantity);

        var transferMovements = dbContext.StockMovements.Where(x => x.MovementType == "transfer").ToList();
        Assert.Equal(2, transferMovements.Count);
        Assert.Equal(0m, transferMovements.Sum(x => x.Quantity));
        Assert.Contains(transferMovements, x => x.LocationCode == SourceLocation && x.Quantity == -4m);
        Assert.Contains(transferMovements, x => x.LocationCode == TargetLocation && x.Quantity == 4m);
        // 全库存量守恒：调拨不产生也不消灭库存。
        Assert.Equal(10m, dbContext.StockLedgers.Sum(x => x.OnHandQuantity));
    }

    [Fact]
    public async Task Balanced_transfer_replay_is_idempotent_on_both_legs()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await SeedSourceStockAsync(handler, dbContext, 10m);

        var command = TransferCommand("idem-transfer-replay", quantity: -4m, transferInLocationCode: TargetLocation, transferInQuantity: 4m);
        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.MovementId, replay.MovementId);
        Assert.Equal(first.TransferInMovementId, replay.TransferInMovementId);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType == "transfer"));
        Assert.Equal(10m, dbContext.StockLedgers.Sum(x => x.OnHandQuantity));
    }

    [Fact]
    public async Task Non_transfer_movement_cannot_carry_a_transfer_in_leg()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() => handler.Handle(
            InboundCommand("idem-inbound-with-leg", 5m) with
            {
                TransferInLocationCode = TargetLocation,
                TransferInQuantity = 5m,
            },
            CancellationToken.None));

        Assert.Equal(InventoryPostingFailureCodes.TransferLegsUnbalanced, exception.FailureCode);
        Assert.Contains("只有调拨", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbound_and_outbound_movements_keep_their_single_leg_behaviour()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);

        await handler.Handle(InboundCommand("idem-inbound-keep", 10m), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var outbound = await handler.Handle(
            InboundCommand("idem-outbound-keep", -3m) with { MovementType = "outbound" },
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Null(outbound.TransferInMovementId);
        Assert.Equal(7m, Assert.Single(dbContext.StockLedgers).OnHandQuantity);
        Assert.Equal(2, dbContext.StockMovements.Count());
    }

    private static async Task SeedSourceStockAsync(
        PostStockMovementCommandHandler handler,
        ApplicationDbContext dbContext,
        decimal quantity)
    {
        await handler.Handle(InboundCommand("idem-transfer-seed", quantity), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static PostStockMovementCommand InboundCommand(string idempotencyKey, decimal quantity)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-TRANSFER-001",
            "LINE-001",
            idempotencyKey,
            "SKU-TRANSFER",
            "EA",
            "SITE-001",
            SourceLocation,
            "LOT-001",
            null,
            "unrestricted",
            "owned",
            null,
            quantity,
            UnitCost: 5m);
    }

    private static PostStockMovementCommand TransferCommand(
        string idempotencyKey,
        decimal quantity,
        string? transferInLocationCode,
        decimal? transferInQuantity)
    {
        return InboundCommand(idempotencyKey, quantity) with
        {
            MovementType = "transfer",
            TransferInLocationCode = transferInLocationCode,
            TransferInQuantity = transferInQuantity,
        };
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-transfer-balance-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopTransferMediator());
    }

    private sealed class NoopTransferMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");
    }
}
