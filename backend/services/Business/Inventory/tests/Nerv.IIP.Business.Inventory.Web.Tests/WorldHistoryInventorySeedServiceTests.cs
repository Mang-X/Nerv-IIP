using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// L1 背景历史（库存域侧）的常规门禁测试：形状、确定性、幂等、隔离、现存量恒等式与 fail-closed。
/// 全量规模下的真实数据库耗时实测在 <see cref="WorldHistoryInventorySeedPostgresTests"/>（env-gated）。
/// </summary>
public sealed class WorldHistoryInventorySeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库写入类用例的规模：足够跑出全部六条链路，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_movement_stream_matches_the_world_bible_shape()
    {
        var movements = WorldHistoryInventorySpec.BuildMovements(AsOfDate, 1.0d);
        var workOrders = WorldHistoryPhase2Spec.BuildWorkOrderFacts(AsOfDate, 1.0d);

        foreach (var group in movements.GroupBy(x => x.MovementType).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"inventory-world-history-movements-{group.Key}={group.Count()}");
        }

        foreach (var group in movements
                     .GroupBy(x => x.Purpose.Split(':')[0], StringComparer.Ordinal)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"inventory-world-history-purpose-{group.Key}={group.Count()}");
        }

        var opening = movements
            .Where(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.OpeningPurpose, StringComparison.Ordinal))
            .Sum(x => x.Quantity);
        var inbound = movements.Where(x => x.Quantity > 0m).Sum(x => x.Quantity);
        var outbound = movements.Where(x => x.Quantity < 0m).Sum(x => -x.Quantity);
        var closing = movements.Sum(x => x.Quantity);
        var ledgerDimensions = movements.Select(x => x.DimensionKey).Distinct(StringComparer.Ordinal).Count();
        var lots = movements.Select(x => x.LotNo ?? "-").Distinct(StringComparer.Ordinal).Count();

        output.WriteLine($"inventory-world-history-movements-total={movements.Count}");
        output.WriteLine($"inventory-world-history-ledger-dimensions={ledgerDimensions}");
        output.WriteLine($"inventory-world-history-distinct-lots={lots}");
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-opening-total={opening}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-inbound-total={inbound}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-outbound-total={outbound}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-closing-total={closing}"));
        output.WriteLine($"inventory-world-history-work-orders={workOrders.Count}");

        // 现存量恒等式：期初 + 入 − 出 == 结存（入库合计已含期初）。
        Assert.Equal(inbound - outbound, closing);
        Assert.True(opening > 0m);
        Assert.True(closing > 0m);

        // 六条链路必须都在。
        foreach (var purpose in new[]
                 {
                     WorldHistoryInventorySpec.OpeningPurpose,
                     WorldHistoryInventorySpec.ReceiptInPurpose,
                     WorldHistoryInventorySpec.MaterialIssueOutPurpose,
                     WorldHistoryInventorySpec.FinishedGoodsInPurpose,
                     WorldHistoryInventorySpec.DeliveryOutPurpose,
                     WorldHistoryInventorySpec.ScrapAdjustmentPurpose,
                 })
        {
            Assert.Contains(movements, x => string.Equals(x.Purpose, purpose, StringComparison.Ordinal));
        }

        // 完工入库条数 == 已完工工单数；发货条数 == 已发货订单数。
        Assert.Equal(
            workOrders.Count(x => x.HasFinishedGoodsReceipt),
            movements.Count(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.FinishedGoodsInPurpose, StringComparison.Ordinal)));
        Assert.Equal(
            WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d).Count(x => x.HasDelivery),
            movements.Count(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.DeliveryOutPurpose, StringComparison.Ordinal)));
    }

    [Fact]
    public void Movement_keys_are_unique_and_the_stream_is_time_ordered()
    {
        var movements = WorldHistoryInventorySpec.BuildMovements(AsOfDate, 0.2d);

        Assert.Equal(movements.Count, movements.Select(x => x.MovementKey).Distinct(StringComparer.Ordinal).Count());
        for (var index = 1; index < movements.Count; index++)
        {
            Assert.True(
                movements[index].PostedAtUtc >= movements[index - 1].PostedAtUtc,
                $"流水 {movements[index].MovementKey} 的过账时间早于上一笔，写入会被负结存拒绝。");
            Assert.Equal(index + 1, movements[index].Sequence);
        }
    }

    [Fact]
    public void Chronological_replay_never_drives_on_hand_negative()
    {
        var running = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var movement in WorldHistoryInventorySpec.BuildMovements(AsOfDate, 0.2d))
        {
            var next = running.GetValueOrDefault(movement.DimensionKey) + movement.Quantity;
            Assert.True(next >= 0m, $"{movement.MovementKey} 把 {movement.DimensionKey} 的现存量打成 {next}。");
            running[movement.DimensionKey] = next;
        }
    }

    [Fact]
    public void All_movement_timestamps_stay_inside_the_history_window_and_off_sunday()
    {
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var movement in WorldHistoryInventorySpec.BuildMovements(AsOfDate, 0.2d))
        {
            Assert.InRange(movement.PostedAtUtc, lowerBound, upperBound);
            Assert.True(
                WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(movement.PostedAtUtc.UtcDateTime)),
                $"{movement.MovementKey} 的过账时间落在周日。");
        }
    }

    [Fact]
    public void Movement_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistoryInventorySpec.BuildMovements(AsOfDate, 0.2d);
        var second = WorldHistoryInventorySpec.BuildMovements(AsOfDate, 0.2d);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Scrap_adjustments_stay_within_the_work_order_scrap_allowance()
    {
        var scrap = WorldHistoryInventorySpec.BuildMovements(AsOfDate, 1.0d)
            .Where(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.ScrapAdjustmentPurpose, StringComparison.Ordinal))
            .ToArray();
        var allowance = WorldHistoryPhase2Spec.BuildWorkOrderFacts(AsOfDate, 1.0d).Sum(x => x.Plan.ScrapQuantity);
        var scrapped = scrap.Sum(x => -x.Quantity);

        output.WriteLine(FormattableString.Invariant($"inventory-world-history-scrap-adjustment-total={scrapped}"));
        output.WriteLine(FormattableString.Invariant($"inventory-world-history-work-order-scrap-total={allowance}"));

        Assert.NotEmpty(scrap);
        Assert.All(scrap, movement => Assert.StartsWith("INV-SCRAP-", movement.IdempotencyKey, StringComparison.Ordinal));
        Assert.True(scrapped > 0m && scrapped <= allowance);
    }

    [Fact]
    public void Hold_status_transfers_are_applied_and_released_in_pairs()
    {
        var movements = WorldHistoryInventorySpec.BuildMovements(AsOfDate, 0.2d);
        var applied = movements.Where(x => string.Equals(x.MovementType, WorldHistoryInventorySpec.StatusTransferOut, StringComparison.Ordinal)).ToArray();
        var released = movements.Where(x => string.Equals(x.MovementType, WorldHistoryInventorySpec.StatusTransferIn, StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(applied);
        Assert.Equal(applied.Length, released.Length);
        Assert.Equal(applied.Sum(x => -x.Quantity), released.Sum(x => x.Quantity));

        for (var index = 0; index < WorldHistoryInventorySpec.StatusTransferOutPurposes.Length; index++)
        {
            var outPurpose = WorldHistoryInventorySpec.StatusTransferOutPurposes[index];
            var inPurpose = WorldHistoryInventorySpec.StatusTransferInPurposes[index];
            Assert.Equal(
                movements.Count(x => string.Equals(x.Purpose, outPurpose, StringComparison.Ordinal)),
                movements.Count(x => string.Equals(x.Purpose, inPurpose, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task Seed_writes_the_full_stream_and_reruns_without_writing_anything()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var facts = WorldHistoryInventorySpec.BuildMovements(AsOfDate, SmallScale);
        output.WriteLine($"small-scale-locations={first.StockLocationsWritten}");
        output.WriteLine($"small-scale-movements={first.StockMovementsWritten}");
        output.WriteLine($"small-scale-ledgers={first.StockLedgersCreated}");
        output.WriteLine($"small-scale-distinct-lots={first.Validation.DistinctLotsChecked}");
        output.WriteLine(FormattableString.Invariant($"small-scale-opening={first.Validation.OpeningQuantityTotal}"));
        output.WriteLine(FormattableString.Invariant($"small-scale-inbound={first.Validation.InboundQuantityTotal}"));
        output.WriteLine(FormattableString.Invariant($"small-scale-outbound={first.Validation.OutboundQuantityTotal}"));
        output.WriteLine(FormattableString.Invariant($"small-scale-closing={first.Validation.ClosingQuantityTotal}"));
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"small-scale-sample: {line}");
        }

        Assert.Equal(WorldHistoryPhase2Spec.StockLocations.Count, first.StockLocationsWritten);
        Assert.Equal(facts.Count, first.StockMovementsWritten);
        Assert.True(first.StockLedgersCreated > 0);

        Assert.Equal(0, second.StockLocationsWritten);
        Assert.Equal(0, second.StockMovementsWritten);
        Assert.Equal(0, second.StockLedgersCreated);
        Assert.Equal(facts.Count, await db.StockMovements.CountAsync());
        Assert.Equal(first.StockLedgersCreated, await db.StockLedgers.CountAsync());
    }

    [Fact]
    public async Task Seeded_ledgers_balance_to_the_opening_plus_inbound_minus_outbound_identity()
    {
        await using var db = CreateDbContext();
        var report = await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var validation = report.Validation;
        Assert.Equal(
            validation.InboundQuantityTotal - validation.OutboundQuantityTotal,
            validation.ClosingQuantityTotal);
        Assert.Equal(validation.ClosingQuantityTotal, await db.StockLedgers.SumAsync(x => x.OnHandQuantity));
        Assert.True(await db.StockLedgers.AllAsync(x => x.OnHandQuantity >= 0m));
        Assert.Equal(WorldHistoryConsistencyValidator.SampleSize, validation.Sample.Count);
    }

    [Fact]
    public async Task Seeded_documents_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var documentIds = await db.StockMovements.Select(x => x.SourceDocumentId).Distinct().ToArrayAsync();
        var lots = await db.StockMovements.Select(x => x.LotNo).Distinct().ToArrayAsync();
        var locations = await db.StockLocations.Select(x => x.LocationCode).ToArrayAsync();

        Assert.NotEmpty(documentIds);
        foreach (var value in documentIds.Concat(lots!).Concat(locations))
        {
            Assert.DoesNotContain("-DEMO-", value, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", value, StringComparison.Ordinal);
        }

        Assert.All(locations, code => Assert.StartsWith("WH-WB-", code, StringComparison.Ordinal));
        Assert.All(await db.StockMovements.Select(x => x.SourceService).Distinct().ToArrayAsync(),
            service => Assert.Equal(WorldHistoryInventorySpec.SourceService, service));
    }

    [Fact]
    public async Task Seed_leaves_the_reserved_leader_demo_stock_untouched()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");
        db.ChangeTracker.Clear();

        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var demoLedger = await db.StockLedgers.SingleAsync(x => x.LocationCode == LeaderDemoSeedService.LocationCode);
        Assert.Equal(LeaderDemoSeedService.RawMaterialSkuCode, demoLedger.SkuCode);
        Assert.Equal(100m, demoLedger.OnHandQuantity);
        Assert.Null(demoLedger.LotNo);
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_movement_disappears()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var movement = await db.StockMovements.FirstAsync(x =>
            x.MovementType == WorldHistoryInventorySpec.Outbound);
        db.StockMovements.Remove(movement);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryInventoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.NotEmpty(exception.Failures);
        Assert.Contains(exception.Failures, failure => failure.Contains("未落库", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_ledger_balance_is_tampered_with()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var ledger = await db.StockLedgers.FirstAsync(x => x.OnHandQuantity > 0m);
        ledger.ApplyMovement(Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate.StockMovement.Post(
            "org-001", "env-dev", WorldHistoryInventorySpec.Inbound, "tenant-manual", "MANUAL-0001", null,
            "manual-0001", ledger.SkuCode, ledger.UomCode, ledger.SiteCode, ledger.LocationCode, ledger.LotNo, null,
            ledger.QualityStatus, ledger.OwnerType, null, 7m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryInventoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("不平", StringComparison.Ordinal));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryTestMediator());
    }

    private sealed class WorldHistoryTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
