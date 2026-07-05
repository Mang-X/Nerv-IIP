using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MediatR;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Expiry;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.MasterData;
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
    public async Task Inbound_movement_derives_expiry_from_sku_shelf_life_policy_when_request_omits_it()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(
            dbContext,
            new FakeSkuExpiryPolicyProvider(new InventorySkuExpiryPolicy(90, null)));

        await handler.Handle(new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-SKU-POLICY",
            "LINE-001",
            "idem-in-sku-policy",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-SKU-POLICY",
            null,
            "qualified",
            "company",
            "owner-001",
            5m,
            ProductionDate: new DateOnly(2026, 7, 1)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 29), dbContext.StockLedgers.Single().ExpiryDate);
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
    public async Task Fefo_reservation_does_not_report_shortage_after_first_100_candidate_batches()
    {
        await using var dbContext = CreateContext();
        for (var index = 1; index <= 101; index++)
        {
            await SeedLedgerAsync(dbContext, $"LOT-{index:000}", Today.AddDays(index), 1m);
        }

        var handler = new ReserveFefoStockCommandHandler(dbContext);

        var result = await handler.Handle(new ReserveFefoStockCommand(
            "org-001",
            "env-dev",
            "wms",
            "OUT-FEFO-101",
            "LINE-001",
            "idem-fefo-101",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "qualified",
            "company",
            "owner-001",
            101m,
            AsOfDate: Today), CancellationToken.None);

        Assert.Equal(101m, result.ReservedQuantity);
        Assert.Equal(101, result.Allocations.Count);
        Assert.Equal("LOT-101", result.Allocations.Last().LotNo);
    }

    [Fact]
    public async Task Fefo_idempotency_replay_returns_original_reserved_quantity_after_partial_allocation()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-FEFO", Today.AddDays(30), 5m);
        var handler = new ReserveFefoStockCommandHandler(dbContext);
        var command = new ReserveFefoStockCommand(
            "org-001",
            "env-dev",
            "wms",
            "OUT-FEFO-REPLAY",
            "LINE-001",
            "idem-fefo-replay",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "qualified",
            "company",
            "owner-001",
            4m,
            AsOfDate: Today);

        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var ledger = dbContext.StockLedgers.Single(x => x.LotNo == "LOT-FEFO");
        var reservation = dbContext.StockReservations.Single();
        ledger.AllocateReservation(reservation, 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var replay = await handler.Handle(command, CancellationToken.None);

        var allocation = Assert.Single(replay.Allocations);
        Assert.Equal(4m, allocation.ReservedQuantity);
        Assert.Equal(4m, replay.ReservedQuantity);
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

    [Fact]
    public async Task Expiry_alert_query_uses_sku_near_expiry_policy_when_threshold_is_not_explicit()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-SKU-THRESHOLD", Today.AddDays(45), 4m);
        var handler = new ListStockExpiryAlertsQueryHandler(
            dbContext,
            new FakeSkuExpiryPolicyProvider(new InventorySkuExpiryPolicy(null, 60)));

        var result = await handler.Handle(new ListStockExpiryAlertsQuery(
            "org-001",
            "env-dev",
            "SITE-01",
            SkuCode: "SKU-FEFO",
            AsOfDate: Today), CancellationToken.None);

        var alert = Assert.Single(result.Items);
        Assert.True(alert.IsNearExpiry);
        Assert.Equal(45, alert.DaysUntilExpiry);
    }

    [Fact]
    public async Task Expired_stock_blocking_moves_available_expired_quantity_to_blocked_status_with_batch_dates()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-BLOCK", new DateOnly(2026, 7, 1), 5m);
        var ledger = dbContext.StockLedgers.Single();
        var reservation = StockReservation.Reserve(ledger, "wms", "OUT-BLOCK", "LINE-001", "idem-block-res", 2m);
        ledger.Reserve(reservation);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new ExpiredStockBlockingService(
            dbContext,
            Options.Create(new ExpiredStockBlockingOptions { Enabled = true }));

        var count = await service.BlockExpiredAvailableStockAsync(Today, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(2m, ledger.OnHandQuantity);
        Assert.Equal(2m, ledger.ReservedQuantity);
        var blocked = Assert.Single(dbContext.StockLedgers, x => x.QualityStatus == "blocked");
        Assert.Equal(3m, blocked.OnHandQuantity);
        Assert.Equal(new DateOnly(2026, 6, 1), blocked.ProductionDate);
        Assert.Equal(new DateOnly(2026, 7, 1), blocked.ExpiryDate);
    }

    [Fact]
    public async Task Inbound_movement_rejects_shelf_life_that_overflows_date_range_as_business_error()
    {
        await using var dbContext = CreateContext();
        var handler = new PostStockMovementCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<InventoryPostingRejectedException>(() =>
            handler.Handle(new PostStockMovementCommand(
                "org-001",
                "env-dev",
                "inbound",
                "wms",
                "IN-OVERFLOW",
                "LINE-001",
                "idem-in-overflow",
                "SKU-FEFO",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-OVERFLOW",
                null,
                "qualified",
                "company",
                "owner-001",
                5m,
                ProductionDate: new DateOnly(9999, 12, 30),
                ShelfLifeDays: 10), CancellationToken.None));

        Assert.Contains("shelf life", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private sealed class FakeSkuExpiryPolicyProvider(InventorySkuExpiryPolicy policy) : IInventorySkuExpiryPolicyProvider
    {
        public Task<InventorySkuExpiryPolicy?> GetAsync(
            string organizationId,
            string environmentId,
            string skuCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<InventorySkuExpiryPolicy?>(policy);
        }
    }
}
