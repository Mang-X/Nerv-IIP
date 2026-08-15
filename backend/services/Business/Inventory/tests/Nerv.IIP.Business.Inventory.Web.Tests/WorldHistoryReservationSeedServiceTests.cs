using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// L1 背景历史 **四期**（库存预留 <c>stock_reservations</c>）的门禁测试。
///
/// 关键约束：任意 asOfDate 都必须成立——演示日期一改，预留规模、已释放 / 未释放的比例、
/// 「已占用 / 可用」两列有值、以及**现存量恒等式不被扰动**都不能塌。因此规模 / 分布 /
/// 一致性类断言一律走 5 日期 <c>[Theory]</c>。
/// </summary>
public sealed class WorldHistoryReservationSeedServiceTests(ITestOutputHelper output)
{
    /// <summary>五个演示候选日期：周日后首日 / 常规日 / 月初 / 春节段 / 月末。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new() { { 2026, 7, 27 }, { 2026, 7, 24 }, { 2026, 8, 3 }, { 2026, 2, 16 }, { 2026, 7, 31 } };

    /// <summary>库写入类用例的规模：全量 29 周在 InMemory 上过慢，0.25 仍能出数百条预留，且春节短历史段也够出齐套预留。</summary>
    private const double SmallScale = 0.25d;

    #region 纯函数 Spec：规模 / 家族 / 维度

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Reservation_plans_keep_their_shape_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var plans = WorldHistoryReservationSpec.BuildReservations(asOfDate, 1.0);
        var movements = WorldHistoryInventorySpec.BuildMovements(asOfDate, 1.0);

        var open = plans.Where(x => x.IsOpen).ToArray();
        var released = plans.Where(x => !x.IsOpen).ToArray();
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} reservations={plans.Count} open={open.Length} "
            + $"released={released.Length} open-quantity={open.Sum(x => x.Quantity):0.##}");

        // 两个家族都必须非空：只有已释放的历史等于「已占用」列还是死的；只有 open 的等于没有释放证据。
        Assert.NotEmpty(open);
        Assert.NotEmpty(released);
        Assert.All(open, plan => Assert.Equal(WorldHistoryReservationKind.WorkOrderKit, plan.Kind));
        Assert.All(released, plan => Assert.Equal(WorldHistoryReservationKind.DeliveryPick, plan.Kind));

        // 已释放家族与发货流水一一对应，预留量与实发量逐件相等。
        var deliveries = movements
            .Where(x => x.Purpose == WorldHistoryInventorySpec.DeliveryOutPurpose)
            .ToDictionary(x => WorldHistoryPhase2Spec.OutboundOrderNo(x.SourceDocumentId), StringComparer.Ordinal);
        Assert.Equal(deliveries.Count, released.Length);
        Assert.All(released, plan =>
        {
            var delivery = Assert.Contains(plan.SourceDocumentId, deliveries);
            Assert.Equal(-delivery.Quantity, plan.Quantity);
            Assert.Equal(delivery.DimensionKey, plan.DimensionKey);
            Assert.True(plan.CreatedAtUtc <= plan.ReleasedAtUtc);
        });

        // 未释放家族一律挂在期初批上（原料 / 半成品的常驻库位），数量为正。
        Assert.All(open, plan =>
        {
            Assert.StartsWith("LOT-OPENING-", plan.LotNo!, StringComparison.Ordinal);
            Assert.StartsWith("WO-2026-", plan.SourceDocumentId, StringComparison.Ordinal);
            Assert.True(plan.Quantity > 0m);
        });

        // 幂等键唯一（唯一索引是 org+env+源服务+源单据+幂等键）。
        Assert.Equal(plans.Count, plans.Select(x => x.ReservationKey).Distinct(StringComparer.Ordinal).Count());

        // 每条预留的维度都必须是真实存在的流水维度，否则就是指向空台账的死行。
        var movementDimensions = movements.Select(x => x.DimensionKey).ToHashSet(StringComparer.Ordinal);
        Assert.All(plans, plan => Assert.Contains(plan.DimensionKey, movementDimensions));
    }

    /// <summary>演示基准日全量 29 周的规模，写入 PR 实测表。</summary>
    [Fact]
    public void Full_history_at_the_demo_baseline_date_lands_in_the_expected_volume()
    {
        var asOfDate = new DateOnly(2026, 7, 28);
        var plans = WorldHistoryReservationSpec.BuildReservations(asOfDate, 1.0);
        var open = plans.Where(x => x.IsOpen).ToArray();

        output.WriteLine($"@scale=1.0 as-of=2026-07-28 reservations={plans.Count} open={open.Length} "
            + $"released={plans.Count - open.Length} "
            + $"open-dimensions={open.Select(x => x.DimensionKey).Distinct(StringComparer.Ordinal).Count()}");

        Assert.InRange(plans.Count, 2000, 4200);
        Assert.InRange(open.Length, 200, 700);
    }

    /// <summary>未释放预留的失效时刻必须落在截止日之后，否则过期扫描会把「已占用」列悄悄清零。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Open_reservations_never_expire_inside_the_demo_window(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var expiresAtUtc = WorldHistoryReservationSpec.OpenReservationExpiresAtUtc(asOfDate);
        Assert.True(expiresAtUtc > new DateTimeOffset(asOfDate, TimeOnly.MaxValue, TimeSpan.Zero));
        Assert.Equal(
            asOfDate.AddDays(WorldHistoryReservationSpec.OpenReservationExpiryDays),
            DateOnly.FromDateTime(expiresAtUtc.UtcDateTime));
    }

    [Fact]
    public void Reservation_plans_are_deterministic()
    {
        var asOfDate = new DateOnly(2026, 7, 24);
        Assert.Equal(
            WorldHistoryReservationSpec.BuildReservations(asOfDate, 1.0),
            WorldHistoryReservationSpec.BuildReservations(asOfDate, 1.0));
    }

    #endregion

    #region 库写入：幂等 + 恒等式硬断言

    /// <summary>
    /// ② 硬断言：预留只改 <c>ReservedQuantity</c> / <c>LedgerVersion</c>，
    /// <c>OnHandQuantity</c> 与「现存量 = 世界观流水代数和」的恒等式逐条不变。
    /// </summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Reservations_only_move_reserved_quantity_and_ledger_version(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();

        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var before = await db.StockLedgers
            .AsNoTracking()
            .Select(x => new { x.SkuCode, x.LocationCode, x.LotNo, x.QualityStatus, x.OnHandQuantity, x.ReservedQuantity, x.LedgerVersion })
            .ToArrayAsync();
        var movementsBefore = await db.StockMovements.AsNoTracking().CountAsync();

        var report = await new WorldHistoryReservationSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var after = await db.StockLedgers
            .AsNoTracking()
            .Select(x => new { x.SkuCode, x.LocationCode, x.LotNo, x.QualityStatus, x.OnHandQuantity, x.ReservedQuantity, x.LedgerVersion })
            .ToArrayAsync();
        var movementsAfter = await db.StockMovements.AsNoTracking().CountAsync();

        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} written={report.StockReservationsWritten} "
            + $"open={report.OpenReservationsWritten} skipped={report.PlansSkippedWithoutLedger} "
            + $"not-kitted={report.PlansSkippedNotKitted} "
            + $"reserved-total={report.Validation.ReservedQuantityTotal:0.##} "
            + $"committed-ledgers={report.Validation.LedgersWithReservationChecked} movements={movementsAfter}");

        Assert.True(report.StockReservationsWritten > 0);
        Assert.True(report.OpenReservationsWritten > 0);

        // 一笔流水都没有新增（预留不是移动）。
        Assert.Equal(movementsBefore, movementsAfter);

        // 台账数量不变，且每条台账的现存量逐条不变——恒等式的直接证据。
        Assert.Equal(before.Length, after.Length);
        var beforeByKey = before.ToDictionary(x => $"{x.SkuCode}|{x.LocationCode}|{x.LotNo}|{x.QualityStatus}", StringComparer.Ordinal);
        var committedLedgers = 0;
        foreach (var ledger in after)
        {
            var key = $"{ledger.SkuCode}|{ledger.LocationCode}|{ledger.LotNo}|{ledger.QualityStatus}";
            var original = Assert.Contains(key, beforeByKey);
            Assert.Equal(original.OnHandQuantity, ledger.OnHandQuantity);
            Assert.Equal(0m, original.ReservedQuantity);
            Assert.True(ledger.ReservedQuantity >= 0m);
            Assert.True(ledger.OnHandQuantity - ledger.ReservedQuantity >= 0m);

            if (ledger.ReservedQuantity > 0m)
            {
                // 只有真正被占用的台账才允许版本推进（Reserve 会 LedgerVersion++）。
                Assert.True(ledger.LedgerVersion > original.LedgerVersion);
                committedLedgers++;
            }
            else
            {
                Assert.Equal(original.LedgerVersion, ledger.LedgerVersion);
            }
        }

        Assert.True(committedLedgers > 0);
        Assert.Equal(committedLedgers, report.Validation.LedgersWithReservationChecked);
        Assert.True(report.Validation.ReservedQuantityTotal > 0m);

        // 校验器复核后现存量恒等式仍成立（重跑主校验器，它是按流水代数和独立重算的）。
        var recheck = await new WorldHistoryConsistencyValidator(db)
            .ValidateAsync("org-001", "env-dev", asOfDate, SmallScale);
        Assert.Equal(movementsAfter, recheck.StockMovementsChecked);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Seed_writes_reservations_idempotently(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();

        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var seed = new WorldHistoryReservationSeedService(db);
        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var reservedAfterFirst = await db.StockLedgers.AsNoTracking().SumAsync(x => x.ReservedQuantity);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var reservations = await db.StockReservations.AsNoTracking().ToArrayAsync();
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} persisted={reservations.Length} "
            + $"reserved={reservedAfterFirst:0.##} validator={first.Validation.StockReservationsChecked}");

        Assert.Equal(first.StockReservationsWritten, reservations.Length);
        Assert.Equal(reservations.Length, first.Validation.StockReservationsChecked);

        // 幂等：重跑写入量为 0，终态不变（占用量绝不叠加）。
        Assert.Equal(0, second.StockReservationsWritten);
        Assert.Equal(0, second.OpenReservationsWritten);
        Assert.Equal(reservations.Length, await db.StockReservations.CountAsync());
        Assert.Equal(reservedAfterFirst, await db.StockLedgers.AsNoTracking().SumAsync(x => x.ReservedQuantity));

        // 状态分布：已释放的 OpenQuantity 为 0，未释放的仍占着。
        Assert.All(reservations, reservation =>
        {
            Assert.Contains(reservation.Status, new[] { "open", "released" });
            Assert.Equal(reservation.Status == "open" ? reservation.ReservedQuantity : 0m, reservation.OpenQuantity);
            Assert.Equal(WorldHistoryReservationSpec.SourceService, reservation.SourceService);
        });
        Assert.Contains(reservations, x => x.Status == "open");
        Assert.Contains(reservations, x => x.Status == "released");
    }

    /// <summary>校验器是 fail-closed 的：手工把一条台账的占用量改坏必须让校验当场失败。</summary>
    [Fact]
    public async Task Validator_rejects_a_ledger_whose_reserved_quantity_drifts()
    {
        var asOfDate = new DateOnly(2026, 7, 24);
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        await new WorldHistoryReservationSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        db.ChangeTracker.Clear();

        var ledger = await db.StockLedgers.FirstAsync(x => x.ReservedQuantity > 0m);
        db.Entry(ledger).Property(x => x.ReservedQuantity).CurrentValue = ledger.ReservedQuantity + 7m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var failure = await Assert.ThrowsAsync<WorldHistoryInventoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateReservationsAsync("org-001", "env-dev", asOfDate, SmallScale));
        output.WriteLine(failure.Message);
        Assert.Contains("占用量", failure.Message, StringComparison.Ordinal);
    }

    #endregion

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-world-history-reservations-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new ReservationTestMediator());
    }

    private sealed class ReservationTestMediator : IMediator
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
