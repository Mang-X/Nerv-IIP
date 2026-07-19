using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
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
        Assert.Null(ledger.ShelfLifeDays);
        Assert.Equal("direct", ledger.ExpiryDateSource);
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

        var ledger = dbContext.StockLedgers.Single();
        Assert.Equal(new DateOnly(2026, 9, 29), ledger.ExpiryDate);
        Assert.Equal(90, ledger.ShelfLifeDays);
        Assert.Equal("derived", ledger.ExpiryDateSource);
    }

    [Fact]
    public async Task Outbound_movement_preserves_existing_derived_expiry_provenance()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-DERIVED-OUT", Today.AddDays(60), 5m);
        var handler = new PostStockMovementCommandHandler(dbContext);

        await handler.Handle(CreateOutboundCommand("LOT-DERIVED-OUT", Today), CancellationToken.None);

        var ledger = Assert.Single(dbContext.StockLedgers);
        Assert.Equal(30, ledger.ShelfLifeDays);
        Assert.Equal(StockExpiryDateSource.Derived, ledger.ExpiryDateSource);
    }

    [Fact]
    public async Task Inbound_movement_restores_known_provenance_for_an_empty_legacy_ledger()
    {
        await using var dbContext = CreateContext();
        var productionDate = Today.AddDays(-30);
        var expiryDate = Today.AddDays(30);
        dbContext.StockLedgers.Add(StockLedger.Create(
            "org-001", "env-dev", "SKU-FEFO", "kg", "SITE-01", "LOC-A-01",
            "LOT-LEGACY-EMPTY", null, "qualified", "company", "owner-001",
            ProductionDate: productionDate, ExpiryDate: expiryDate));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new PostStockMovementCommandHandler(dbContext).Handle(
            CreateInboundCommand("LOT-LEGACY-EMPTY", productionDate, expiryDate),
            CancellationToken.None);

        var ledger = Assert.Single(dbContext.StockLedgers);
        Assert.Equal(5m, ledger.OnHandQuantity);
        Assert.Equal(StockExpiryDateSource.Direct, ledger.ExpiryDateSource);
        Assert.Null(ledger.ShelfLifeDays);
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
    public async Task Expiry_alert_query_returns_server_total_and_requested_page()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-001", Today.AddDays(1), 1m);
        await SeedLedgerAsync(dbContext, "LOT-002", Today.AddDays(2), 1m);
        await SeedLedgerAsync(dbContext, "LOT-003", Today.AddDays(3), 1m);
        var handler = new ListStockExpiryAlertsQueryHandler(dbContext);

        var result = await handler.Handle(new ListStockExpiryAlertsQuery(
            "org-001",
            "env-dev",
            "SITE-01",
            AsOfDate: Today,
            NearExpiryThresholdDays: 30,
            Page: 2,
            PageSize: 2), CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(0, result.ExpiredCount);
        Assert.Equal(3, result.NearExpiryCount);
        Assert.Equal(1, result.SkuCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal("LOT-003", Assert.Single(result.Items).LotNo);
    }

    [Fact]
    public async Task Expiry_alert_paging_uses_a_stable_ledger_tie_breaker()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-SAME", Today.AddDays(5), 1m, "SN-003");
        await SeedLedgerAsync(dbContext, "LOT-SAME", Today.AddDays(5), 1m, "SN-001");
        await SeedLedgerAsync(dbContext, "LOT-SAME", Today.AddDays(5), 1m, "SN-002");
        var handler = new ListStockExpiryAlertsQueryHandler(dbContext);

        var firstPage = await handler.Handle(new ListStockExpiryAlertsQuery(
            "org-001", "env-dev", "SITE-01", AsOfDate: Today, Page: 1, PageSize: 2), CancellationToken.None);
        var secondPage = await handler.Handle(new ListStockExpiryAlertsQuery(
            "org-001", "env-dev", "SITE-01", AsOfDate: Today, Page: 2, PageSize: 2), CancellationToken.None);

        Assert.Equal(["SN-001", "SN-002"], firstPage.Items.Select(x => x.SerialNo));
        Assert.Equal("SN-003", Assert.Single(secondPage.Items).SerialNo);
    }

    [Fact]
    public async Task Availability_query_keeps_expiry_dimensions_and_returns_backend_operation_reasons()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-EXPIRED", Today.AddDays(-1), 2m);
        await SeedLedgerAsync(dbContext, "LOT-FRESH", Today.AddDays(60), 3m);
        var expiredLedger = dbContext.StockLedgers.Single(x => x.LotNo == "LOT-EXPIRED");
        expiredLedger.FreezeForCount("COUNT-EXPIRED");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new GetStockAvailabilityQueryHandler(dbContext).Handle(
            new GetStockAvailabilityQuery(
                "org-001",
                "env-dev",
                "SKU-FEFO",
                "kg",
                "SITE-01",
                null,
                null,
                null,
                null,
                null,
                null,
                AsOfDate: Today),
            CancellationToken.None);

        var expired = Assert.Single(result.Items, x => x.LotNo == "LOT-EXPIRED");
        Assert.Equal(Today.AddDays(-31), expired.ProductionDate);
        Assert.Equal(Today.AddDays(-1), expired.ExpiryDate);
        Assert.True(expired.IsExpired);
        Assert.False(expired.MovementAllowed);
        Assert.Equal("expired-stock", expired.MovementBlockReasonCode);
        Assert.False(expired.CountAllowed);
        Assert.Equal("count-frozen", expired.CountBlockReasonCode);

        var fresh = Assert.Single(result.Items, x => x.LotNo == "LOT-FRESH");
        Assert.False(fresh.IsExpired);
        Assert.True(fresh.MovementAllowed);
        Assert.True(fresh.CountAllowed);
    }

    [Fact]
    public async Task Availability_and_expiry_alerts_disable_count_when_the_write_scope_is_ambiguous()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-AMBIGUOUS", Today.AddDays(5), 1m);
        await SeedLedgerAsync(dbContext, "LOT-AMBIGUOUS", Today.AddDays(6), 1m);

        var availability = await new GetStockAvailabilityQueryHandler(dbContext).Handle(
            new GetStockAvailabilityQuery(
                "org-001", "env-dev", "SKU-FEFO", "kg", "SITE-01",
                null, "LOT-AMBIGUOUS", null, null, null, null, Today),
            CancellationToken.None);
        var alerts = await new ListStockExpiryAlertsQueryHandler(dbContext).Handle(
            new ListStockExpiryAlertsQuery(
                "org-001", "env-dev", "SITE-01", SkuCode: "SKU-FEFO", AsOfDate: Today),
            CancellationToken.None);

        Assert.All(availability.Items, item =>
        {
            Assert.False(item.CountAllowed);
            Assert.Equal("count-scope-ambiguous", item.CountBlockReasonCode);
        });
        Assert.All(alerts.Items, item =>
        {
            Assert.False(item.CountAllowed);
            Assert.Equal("count-scope-ambiguous", item.CountBlockReasonCode);
        });
    }

    [Fact]
    public async Task Expiry_alert_count_scope_ignores_identical_dimensions_from_other_tenants()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-TENANT", Today.AddDays(5), 1m);
        var foreign = StockLedger.Create(
            "org-other", "env-other", "SKU-FEFO", "kg", "SITE-01", "LOC-A-01",
            "LOT-TENANT", null, "qualified", "company", "owner-001",
            ProductionDate: Today.AddDays(-25), ExpiryDate: Today.AddDays(5),
            ShelfLifeDays: 30, ExpiryDateSource: StockExpiryDateSource.Derived);
        foreign.ApplyMovement(StockMovement.Post(
            "org-other", "env-other", "inbound", "wms", "IN-FOREIGN", "LINE-001", "idem-foreign",
            "SKU-FEFO", "kg", "SITE-01", "LOC-A-01", "LOT-TENANT", null,
            "qualified", "company", "owner-001", 1m,
            ProductionDate: Today.AddDays(-25), ExpiryDate: Today.AddDays(5)));
        dbContext.StockLedgers.Add(foreign);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListStockExpiryAlertsQueryHandler(dbContext).Handle(
            new ListStockExpiryAlertsQuery(
                "org-001", "env-dev", "SITE-01", SkuCode: "SKU-FEFO", AsOfDate: Today),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.True(item.CountAllowed);
        Assert.Null(item.CountBlockReasonCode);
    }

    [Fact]
    public void Ledger_normalizes_unprovable_expiry_provenance_to_unknown()
    {
        var direct = StockLedger.Create(
            "org-001", "env-dev", "SKU-FEFO", "kg", "SITE-01", "LOC-A-01",
            "LOT-DIRECT", null, "qualified", "company", "owner-001",
            ProductionDate: Today, ExpiryDate: Today.AddDays(30), ShelfLifeDays: 30,
            ExpiryDateSource: StockExpiryDateSource.Direct);
        var invalidDerived = StockLedger.Create(
            "org-001", "env-dev", "SKU-FEFO", "kg", "SITE-01", "LOC-A-01",
            "LOT-INVALID", null, "qualified", "company", "owner-001",
            ProductionDate: Today, ExpiryDate: Today.AddDays(30), ShelfLifeDays: null,
            ExpiryDateSource: StockExpiryDateSource.Derived);

        Assert.Null(direct.ShelfLifeDays);
        Assert.Equal(StockExpiryDateSource.Direct, direct.ExpiryDateSource);
        Assert.Null(invalidDerived.ShelfLifeDays);
        Assert.Null(invalidDerived.ExpiryDateSource);

        var missingDates = StockLedger.Create(
            "org-001", "env-dev", "SKU-FEFO", "kg", "SITE-01", "LOC-A-01",
            "LOT-MISSING-DATES", null, "qualified", "company", "owner-001",
            ShelfLifeDays: 30, ExpiryDateSource: StockExpiryDateSource.Derived);
        Assert.Null(missingDates.ExpiryDateSource);
        Assert.Null(missingDates.ShelfLifeDays);
    }

    [Fact]
    public void Count_scope_filter_translates_exact_page_scopes_for_postgres()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only;Username=test;Password=test")
            .Options;
        using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        var scopes = new[]
        {
            new StockCountScope("org-001", "env-dev", "SKU-A", "kg", "SITE-01", "LOC-A", "LOT-A", null, "qualified", "company", null),
            new StockCountScope("org-001", "env-dev", "SKU-B", "ea", "SITE-01", "LOC-B", null, "SN-B", "blocked", "customer", "OWNER-B"),
        };

        var sql = ListStockExpiryAlertsQueryHandler
            .RestrictToCountScopes(dbContext.StockLedgers.AsNoTracking(), scopes)
            .ToQueryString();

        Assert.Contains("SKU-A", sql, StringComparison.Ordinal);
        Assert.Contains("SKU-B", sql, StringComparison.Ordinal);
        Assert.Contains(" OR ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" IN (", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expiry_query_rejects_page_numbers_that_can_overflow_the_server_offset()
    {
        var result = new ListStockExpiryAlertsQueryValidator().Validate(
            new ListStockExpiryAlertsQuery("org-001", "env-dev", "SITE-01", Page: int.MaxValue, PageSize: 200));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListStockExpiryAlertsQuery.Page));
    }

    [Fact]
    public void Expiry_provenance_database_constraint_is_two_valued_and_matches_derived_date_proof()
    {
        using var dbContext = CreateContext();
        var sql = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StockLedger))!
            .GetCheckConstraints()
            .Single(constraint => constraint.Name == "ck_stock_ledgers_expiry_provenance")
            .Sql;

        Assert.Contains("expiry_date_source is not null", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expiry_date - production_date = shelf_life_days", sql, StringComparison.OrdinalIgnoreCase);
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
            Options.Create(new ExpiredStockBlockingOptions { Enabled = true }),
            new ImmediateStatusTransferSender(dbContext));

        var count = await service.BlockExpiredAvailableStockAsync(Today, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(2m, ledger.OnHandQuantity);
        Assert.Equal(2m, ledger.ReservedQuantity);
        var blocked = Assert.Single(dbContext.StockLedgers, x => x.QualityStatus == "blocked");
        Assert.Equal(3m, blocked.OnHandQuantity);
        Assert.Equal(new DateOnly(2026, 6, 1), blocked.ProductionDate);
        Assert.Equal(new DateOnly(2026, 7, 1), blocked.ExpiryDate);
        Assert.Equal(30, blocked.ShelfLifeDays);
        Assert.Equal("derived", blocked.ExpiryDateSource);
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

    private static async Task SeedLedgerAsync(
        ApplicationDbContext dbContext,
        string lotNo,
        DateOnly expiryDate,
        decimal quantity,
        string? serialNo = null)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            serialNo,
            "qualified",
            "company",
            "owner-001",
            ProductionDate: expiryDate.AddDays(-30),
            ExpiryDate: expiryDate,
            ShelfLifeDays: 30,
            ExpiryDateSource: StockExpiryDateSource.Derived);
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
            serialNo,
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

    private sealed class ImmediateStatusTransferSender(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockStatusTransferCommand command && typeof(TResponse) == typeof(PostStockStatusTransferResult))
            {
                return new PostStockStatusTransferCommandHandler(dbContext)
                    .Handle(command, cancellationToken)
                    .ContinueWith(task => (TResponse)(object)task.Result, cancellationToken);
            }

            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException($"Unsupported request type {typeof(TRequest).Name}.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
    }
}
