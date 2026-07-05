using Microsoft.EntityFrameworkCore;
using MediatR;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryBatchExpiryFefoTests
{
    private static readonly DateOnly Today = new(2026, 7, 5);

    [Fact]
    public async Task Inbound_movement_records_production_and_expiry_dates_on_movement_and_ledger()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);

        await handler.Handle(CreateInboundCommand("LOT-FRESH", new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 1)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var ledger = Assert.Single(dbContext.StockLedgers);
        var movement = Assert.Single(dbContext.StockMovements);
        Assert.Equal(new DateOnly(2026, 7, 1), ledger.ProductionDate);
        Assert.Equal(new DateOnly(2026, 10, 1), ledger.ExpiryDate);
        Assert.Equal(ledger.ProductionDate, movement.ProductionDate);
        Assert.Equal(ledger.ExpiryDate, movement.ExpiryDate);
    }

    [Fact]
    public async Task Expired_batch_cannot_be_reserved_or_posted_without_override_permission()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-EXPIRED", new DateOnly(2026, 1, 1), 5m);

        var reservationHandler = new ReserveStockCommandHandler(dbContext);
        var reserveException = await Assert.ThrowsAsync<KnownException>(() =>
            reservationHandler.Handle(CreateReserveCommand("LOT-EXPIRED", asOfDate: Today), CancellationToken.None));
        Assert.Contains("expired", reserveException.Message, StringComparison.OrdinalIgnoreCase);

        var movementHandler = new PostStockMovementCommandHandler(dbContext);
        var movementException = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() =>
            movementHandler.Handle(CreateOutboundCommand("LOT-EXPIRED", asOfDate: Today), CancellationToken.None));
        Assert.Contains("expired", movementException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_batch_reservation_requires_both_override_flag_and_permission()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-EXPIRED", new DateOnly(2026, 1, 1), 5m);
        var handler = new ReserveStockCommandHandler(dbContext);

        await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(CreateReserveCommand("LOT-EXPIRED", asOfDate: Today, allowExpiredStock: true), CancellationToken.None));

        var result = await handler.Handle(
            CreateReserveCommand("LOT-EXPIRED", asOfDate: Today, allowExpiredStock: true, expiryOverridePermissionGranted: true),
            CancellationToken.None);

        Assert.Equal(1m, result.ReservedQuantity);
        Assert.Equal(4m, result.AvailableQuantity);
    }

    [Fact]
    public async Task Fefo_reservation_uses_earliest_non_expired_batch_for_same_sku()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-LATE", new DateOnly(2026, 9, 30), 5m);
        await SeedLedgerAsync(dbContext, "LOT-EARLY", new DateOnly(2026, 7, 31), 5m);
        var handler = new ReserveFefoStockCommandHandler(dbContext);

        var result = await handler.Handle(new ReserveFefoStockCommand(
            "org-001",
            "env-dev",
            "wms",
            "OUT-FEFO",
            "LINE-001",
            "idem-fefo-001",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "qualified",
            "company",
            "owner-001",
            4m,
            AsOfDate: Today), CancellationToken.None);

        var allocation = Assert.Single(result.Allocations);
        Assert.Equal("LOT-EARLY", allocation.LotNo);
        Assert.Equal(new DateOnly(2026, 7, 31), allocation.ExpiryDate);
        Assert.Equal(1m, dbContext.StockLedgers.Single(x => x.LotNo == "LOT-EARLY").AvailableQuantity);
        Assert.Equal(5m, dbContext.StockLedgers.Single(x => x.LotNo == "LOT-LATE").AvailableQuantity);
    }

    [Fact]
    public async Task Expiry_alert_query_marks_near_expiry_and_expired_batches()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-EXPIRED", new DateOnly(2026, 7, 1), 3m);
        await SeedLedgerAsync(dbContext, "LOT-NEAR", new DateOnly(2026, 7, 12), 4m);
        await SeedLedgerAsync(dbContext, "LOT-FRESH", new DateOnly(2026, 9, 1), 5m);
        var handler = new ListStockExpiryAlertsQueryHandler(dbContext);

        var result = await handler.Handle(new ListStockExpiryAlertsQuery(
            "org-001",
            "env-dev",
            "SITE-01",
            AsOfDate: Today,
            NearExpiryThresholdDays: 10), CancellationToken.None);

        Assert.Contains(result.Items, x => x.LotNo == "LOT-EXPIRED" && x.IsExpired && x.DaysUntilExpiry == -4);
        Assert.Contains(result.Items, x => x.LotNo == "LOT-NEAR" && !x.IsExpired && x.IsNearExpiry && x.DaysUntilExpiry == 7);
        Assert.DoesNotContain(result.Items, x => x.LotNo == "LOT-FRESH" && x.IsNearExpiry);
    }

    private static async Task SeedLedgerAsync(ApplicationDbContext dbContext, string lotNo, DateOnly expiryDate, decimal quantity)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            ProductionDate: expiryDate.AddDays(-30),
            ExpiryDate: expiryDate);
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            $"IN-{lotNo}",
            "LINE-001",
            $"idem-in-{lotNo}",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            quantity,
            ProductionDate: expiryDate.AddDays(-30),
            ExpiryDate: expiryDate));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static PostStockMovementCommand CreateInboundCommand(string lotNo, DateOnly productionDate, DateOnly expiryDate)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            $"IN-{lotNo}",
            "LINE-001",
            $"idem-in-{lotNo}",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            5m,
            ProductionDate: productionDate,
            ExpiryDate: expiryDate);
    }

    private static PostStockMovementCommand CreateOutboundCommand(string lotNo, DateOnly asOfDate)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "outbound",
            "wms",
            $"OUT-{lotNo}",
            "LINE-001",
            $"idem-out-{lotNo}",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            -1m,
            AsOfDate: asOfDate);
    }

    private static ReserveStockCommand CreateReserveCommand(
        string lotNo,
        DateOnly asOfDate,
        bool allowExpiredStock = false,
        bool expiryOverridePermissionGranted = false)
    {
        return new ReserveStockCommand(
            "org-001",
            "env-dev",
            "wms",
            $"OUT-{lotNo}",
            "LINE-001",
            $"idem-reserve-{lotNo}-{allowExpiredStock}-{expiryOverridePermissionGranted}",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            1m,
            AsOfDate: asOfDate,
            AllowExpiredStock: allowExpiredStock,
            ExpiryOverridePermissionGranted: expiryOverridePermissionGranted);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-batch-expiry-fefo-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator only supports publish.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("This test mediator only supports publish.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator only supports publish.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator does not support streams.");
        }
    }
}
