using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **库存域侧**（二期）。
///
/// 覆盖：现存量恒等式（<c>现存量 = 期初 + 入 − 出</c>，从流水独立重算）、按时间回放全程不为负、
/// 完工入库量 ↔ MES 好品产出量、发货量 ↔ 订单数量、报废量不越过一期投料放大量、
/// 不合格品持有痕迹「施加 / 释放」成对、时间戳落在历史区间且不在周日、与固定演示事实隔离。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryInventoryConsistencyException"/>。
///
/// 跨服务的端到端抽样核对不在这里做（库存看不到 MES / Quality 的库）：配对由
/// <see cref="WorldHistoryInventorySpec"/> 的确定性与各侧黄金向量测试保证。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 20;

    private const decimal QuantityTolerance = 0.000001m;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryInventoryValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var facts = WorldHistoryInventorySpec.BuildMovements(asOfDate, scale);
        var factByKey = facts.ToDictionary(fact => fact.MovementKey, StringComparer.Ordinal);
        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale);
        var failures = new List<string>();

        var movements = await LoadMovementsAsync(organizationId, environmentId, cancellationToken);
        var ledgers = await LoadLedgersAsync(organizationId, environmentId, cancellationToken);

        CheckPopulation(factByKey, movements, failures);
        CheckTimestampsAndIsolation(movements, asOfDate, failures);
        CheckLedgerBalances(movements, ledgers, failures);
        CheckChronologicalReplay(movements, factByKey, failures);
        CheckProductionAndDeliveryChain(facts, movements, workOrderFacts, asOfDate, scale, failures);
        CheckScrapBoundary(facts, movements, workOrderFacts, failures);
        CheckHoldPairs(facts, movements, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryInventoryConsistencyException(failures);
        }

        var openingTotal = movements
            .Where(x => IsPurpose(x, factByKey, WorldHistoryInventorySpec.OpeningPurpose))
            .Sum(x => x.Quantity);

        return new WorldHistoryInventoryValidationReport(
            StockMovementsChecked: movements.Count,
            StockLedgersChecked: ledgers.Count,
            DistinctLotsChecked: ledgers.Select(x => x.LotNo ?? "-").Distinct(StringComparer.Ordinal).Count(),
            OpeningQuantityTotal: openingTotal,
            InboundQuantityTotal: movements.Where(x => x.Quantity > 0m).Sum(x => x.Quantity),
            OutboundQuantityTotal: movements.Where(x => x.Quantity < 0m).Sum(x => -x.Quantity),
            ClosingQuantityTotal: ledgers.Sum(x => x.OnHandQuantity),
            Sample: BuildSample(movements, factByKey));
    }

    /// <summary>
    /// 预留块（四期）的 fail-closed 校验，由 <see cref="WorldHistoryReservationSeedService"/> 在写完预留后调用。
    ///
    /// <b>恒等式红线三条款</b>：
    /// <list type="number">
    /// <item><b>预留没有改动现存量</b>——每条台账的 <c>OnHandQuantity</c> 仍等于其维度上全部世界观
    ///       流水的代数和（从流水独立重算，与 <see cref="CheckLedgerBalances"/> 同一算法）；</item>
    /// <item><b>预留没有写出任何流水</b>——世界观流水条数仍等于
    ///       <see cref="WorldHistoryInventorySpec.BuildMovements"/> 的计划条数，
    ///       且没有一笔流水挂在预留的源单据号（<c>OB-*</c>）上；</item>
    /// <item><b>占用量对得上账</b>——每条台账的 <c>ReservedQuantity</c> 恰等于其维度上全部未释放
    ///       预留的 <c>OpenQuantity</c> 之和，且 <c>可用量 = 现存量 − 占用量 ≥ 0</c>。</item>
    /// </list>
    /// 另加：预留逐条与计划一致、未释放预留的失效时刻落在截止日之后（否则过期扫描会把
    /// 「已占用」列重新清零）、时间戳落在历史区间、与固定演示事实 / 规模块隔离。
    /// </summary>
    public async Task<WorldHistoryReservationValidationReport> ValidateReservationsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var movementFacts = WorldHistoryInventorySpec.BuildMovements(asOfDate, scale);
        var plans = WorldHistoryReservationSpec.BuildReservations(asOfDate, scale);
        var failures = new List<string>();

        var movements = await LoadMovementsAsync(organizationId, environmentId, cancellationToken);
        var ledgers = await LoadReservableLedgersAsync(organizationId, environmentId, cancellationToken);
        var reservations = await LoadReservationsAsync(organizationId, environmentId, cancellationToken);

        CheckReservationsWroteNoMovement(movementFacts, movements, failures);
        CheckLedgerBalances(movements, [.. ledgers.Select(x => x.ToLedgerProjection())], failures);
        CheckReservationPopulation(plans, reservations, failures);
        CheckReservedQuantities(ledgers, reservations, failures);
        CheckReservationTimestamps(reservations, asOfDate, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryInventoryConsistencyException(failures);
        }

        return new WorldHistoryReservationValidationReport(
            StockReservationsChecked: reservations.Count,
            OpenReservationsChecked: reservations.Count(x => x.OpenQuantity > 0m),
            ReservedQuantityTotal: ledgers.Sum(x => x.ReservedQuantity),
            AvailableQuantityTotal: ledgers.Sum(x => x.OnHandQuantity - x.ReservedQuantity),
            LedgersWithReservationChecked: ledgers.Count(x => x.ReservedQuantity > 0m),
            Sample: BuildReservationSample(plans));
    }

    #region 预留校验项

    /// <summary>红线二：预留一笔流水都不许写。</summary>
    private static void CheckReservationsWroteNoMovement(
        IReadOnlyList<WorldHistoryStockMovementFact> movementFacts,
        IReadOnlyList<MovementProjection> movements,
        List<string> failures)
    {
        if (movements.Count != movementFacts.Count)
        {
            failures.Add($"世界观流水条数为 {movements.Count}，与计划的 {movementFacts.Count} 不符——"
                + "预留块绝不允许新增或删除任何库存流水。");
        }

        foreach (var stray in movements
            .Where(x => x.SourceDocumentId.StartsWith("OB-", StringComparison.Ordinal))
            .Take(5))
        {
            failures.Add($"库存流水 {stray.Key} 挂在出库单号上——预留不是移动，不得落流水。");
        }
    }

    /// <summary>计划中的每条预留都必须落库，库内也不得出现计划外的世界观预留。</summary>
    private static void CheckReservationPopulation(
        IReadOnlyList<WorldHistoryReservationPlan> plans,
        IReadOnlyList<ReservationProjection> reservations,
        List<string> failures)
    {
        var byKey = reservations.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var planKeys = plans.Select(x => x.ReservationKey).ToHashSet(StringComparer.Ordinal);

        foreach (var extra in reservations.Where(x => !planKeys.Contains(x.Key)).Take(5))
        {
            failures.Add($"库内世界观预留 {extra.Key} 不在本次计划内（号段被外部占用？）。");
        }

        foreach (var plan in plans)
        {
            if (!byKey.TryGetValue(plan.ReservationKey, out var reservation))
            {
                // 缩放边界下台账维度可能不存在，seed 会跳过；此处只对已落库的行做逐条对账。
                continue;
            }

            if (reservation.DimensionKey != plan.DimensionKey)
            {
                failures.Add($"预留 {plan.ReservationKey} 的台账维度 {reservation.DimensionKey} 与计划不符。");
            }

            if (Math.Abs(reservation.ReservedQuantity - plan.Quantity) > QuantityTolerance)
            {
                failures.Add($"预留 {plan.ReservationKey} 的预留量 {reservation.ReservedQuantity} 与计划 {plan.Quantity} 不符。");
            }

            if (!string.Equals(reservation.Status, plan.ExpectedStatus, StringComparison.Ordinal))
            {
                failures.Add($"预留 {plan.ReservationKey} 的状态为 {reservation.Status}，计划期望 {plan.ExpectedStatus}。");
            }

            var expectedOpen = plan.IsOpen ? plan.Quantity : 0m;
            if (Math.Abs(reservation.OpenQuantity - expectedOpen) > QuantityTolerance)
            {
                failures.Add($"预留 {plan.ReservationKey} 的未释放量 {reservation.OpenQuantity} 与计划 {expectedOpen} 不符。");
            }
        }
    }

    /// <summary>红线三：台账占用量 = 该维度上未释放预留之和，且可用量非负。</summary>
    private static void CheckReservedQuantities(
        IReadOnlyList<ReservableLedgerProjection> ledgers,
        IReadOnlyList<ReservationProjection> reservations,
        List<string> failures)
    {
        var openByDimension = reservations
            .Where(x => x.OpenQuantity > 0m)
            .GroupBy(x => x.DimensionKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.OpenQuantity), StringComparer.Ordinal);

        foreach (var ledger in ledgers)
        {
            var expected = openByDimension.GetValueOrDefault(ledger.DimensionKey, 0m);
            if (Math.Abs(ledger.ReservedQuantity - expected) > QuantityTolerance)
            {
                failures.Add($"台账 {ledger.DimensionKey} 的占用量 {ledger.ReservedQuantity} "
                    + $"与其未释放预留之和 {expected} 不平。");
            }

            if (ledger.OnHandQuantity - ledger.ReservedQuantity < -QuantityTolerance)
            {
                failures.Add($"台账 {ledger.DimensionKey} 的可用量为负"
                    + $"（现存 {ledger.OnHandQuantity} − 占用 {ledger.ReservedQuantity}）。");
            }
        }

        var ledgerKeys = ledgers.Select(x => x.DimensionKey).ToHashSet(StringComparer.Ordinal);
        foreach (var orphan in openByDimension.Keys.Where(key => !ledgerKeys.Contains(key)).Take(5))
        {
            failures.Add($"预留维度 {orphan} 上没有对应的台账。");
        }
    }

    /// <summary>预留时间戳落在历史区间；未释放预留的失效时刻必须晚于截止日；与保留号段隔离。</summary>
    private static void CheckReservationTimestamps(
        IReadOnlyList<ReservationProjection> reservations,
        DateOnly asOfDate,
        List<string> failures)
    {
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        foreach (var reservation in reservations)
        {
            if (reservation.CreatedAtUtc < lowerBound || reservation.CreatedAtUtc > upperBound)
            {
                failures.Add($"预留 {reservation.Key} 的创建时间 {reservation.CreatedAtUtc:O} 落在历史区间之外。");
            }

            if (reservation.UpdatedAtUtc < reservation.CreatedAtUtc)
            {
                failures.Add($"预留 {reservation.Key} 的更新时间早于创建时间。");
            }

            // 未释放的预留若带着一个已过期的失效时刻，过期扫描会把它自动释放，
            // 「库存可用量」页的「已占用」列会在演示途中悄悄归零。
            if (reservation.OpenQuantity > 0m && reservation.ExpiresAtUtc <= upperBound)
            {
                failures.Add($"未释放预留 {reservation.Key} 的失效时间 {reservation.ExpiresAtUtc:O} "
                    + "不晚于截止日，过期扫描会把它自动释放。");
            }

            foreach (var infix in ReservedInfixes)
            {
                if (reservation.SourceDocumentId.Contains(infix, StringComparison.Ordinal) ||
                    (reservation.LotNo?.Contains(infix, StringComparison.Ordinal) ?? false))
                {
                    failures.Add($"预留 {reservation.Key} 落进了保留号段 '{infix}'。");
                }
            }
        }
    }

    private static IReadOnlyList<string> BuildReservationSample(IReadOnlyList<WorldHistoryReservationPlan> plans)
    {
        var open = plans.Where(x => x.IsOpen).Take(SampleSize / 2);
        var released = plans.Where(x => !x.IsOpen).Take(SampleSize / 2);
        return
        [
            .. open.Concat(released).Select(plan => string.Create(
                CultureInfo.InvariantCulture,
                $"{plan.CreatedAtUtc:yyyy-MM-dd HH:mm}Z {plan.Kind} {plan.SourceDocumentId} "
                + $"{plan.SkuCode}@{plan.LocationCode}[{plan.LotNo ?? "-"}] 预留={plan.Quantity:0.##} {plan.ExpectedStatus}")),
        ];
    }

    private async Task<IReadOnlyList<ReservationProjection>> LoadReservationsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await dbContext.StockReservations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.SourceService == WorldHistoryReservationSpec.SourceService)
            .Select(x => new ReservationProjection(
                x.SourceDocumentId,
                x.IdempotencyKey,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.QualityStatus,
                x.OwnerType,
                x.ReservedQuantity,
                x.OpenQuantity,
                x.Status,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ExpiresAtUtc))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<ReservableLedgerProjection>> LoadReservableLedgersAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.LotNo != null && x.LotNo.StartsWith("LOT-"))
            .Select(x => new ReservableLedgerProjection(
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.QualityStatus,
                x.OwnerType,
                x.OnHandQuantity,
                x.ReservedQuantity))
            .ToArrayAsync(cancellationToken);

    private sealed record ReservationProjection(
        string SourceDocumentId,
        string IdempotencyKey,
        string SkuCode,
        string UomCode,
        string SiteCode,
        string LocationCode,
        string? LotNo,
        string QualityStatus,
        string OwnerType,
        decimal ReservedQuantity,
        decimal OpenQuantity,
        string Status,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime ExpiresAtUtc)
    {
        public string Key => $"{SourceDocumentId}|{IdempotencyKey}";

        public string DimensionKey =>
            $"{SkuCode}|{UomCode}|{SiteCode}|{LocationCode}|{LotNo ?? "-"}|{QualityStatus}|{OwnerType}";
    }

    private sealed record ReservableLedgerProjection(
        string SkuCode,
        string UomCode,
        string SiteCode,
        string LocationCode,
        string? LotNo,
        string QualityStatus,
        string OwnerType,
        decimal OnHandQuantity,
        decimal ReservedQuantity)
    {
        public string DimensionKey =>
            $"{SkuCode}|{UomCode}|{SiteCode}|{LocationCode}|{LotNo ?? "-"}|{QualityStatus}|{OwnerType}";

        public LedgerProjection ToLedgerProjection() =>
            new(SkuCode, UomCode, SiteCode, LocationCode, LotNo, QualityStatus, OwnerType, OnHandQuantity);
    }

    #endregion

    #region 校验项

    /// <summary>1) 计划中的每一笔流水都必须落库，库内也不得出现计划外的世界观流水。</summary>
    private static void CheckPopulation(
        Dictionary<string, WorldHistoryStockMovementFact> factByKey,
        IReadOnlyList<MovementProjection> movements,
        List<string> failures)
    {
        var present = movements.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in factByKey.Keys.Where(key => !present.Contains(key)).Take(5))
        {
            failures.Add($"计划中的库存流水 {missing} 未落库。");
        }

        foreach (var extra in movements.Where(x => !factByKey.ContainsKey(x.Key)).Take(5))
        {
            failures.Add($"库内世界观流水 {extra.Key} 不在本次计划内（号段被外部占用？）。");
        }
    }

    /// <summary>5) 时间戳落在 [上线日, asOfDate] 内且不在周日；7) 与固定演示事实、规模块隔离。</summary>
    private static void CheckTimestampsAndIsolation(
        IReadOnlyList<MovementProjection> movements,
        DateOnly asOfDate,
        List<string> failures)
    {
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        foreach (var movement in movements)
        {
            if (movement.PostedAtUtc < lowerBound || movement.PostedAtUtc > upperBound)
            {
                failures.Add($"库存流水 {movement.Key} 的过账时间 {movement.PostedAtUtc:O} 落在历史区间之外。");
            }

            if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(movement.PostedAtUtc)))
            {
                failures.Add($"库存流水 {movement.Key} 的过账时间 {movement.PostedAtUtc:O} 落在周日（停产保养日）。");
            }

            foreach (var infix in ReservedInfixes)
            {
                if (movement.SourceDocumentId.Contains(infix, StringComparison.Ordinal) ||
                    (movement.LotNo?.Contains(infix, StringComparison.Ordinal) ?? false))
                {
                    failures.Add($"库存流水 {movement.Key} 落进了保留号段 '{infix}'。");
                }
            }
        }
    }

    /// <summary>2) 现存量恒等式：每条台账的现存量必须等于其维度上全部流水的代数和（从流水独立重算）。</summary>
    private static void CheckLedgerBalances(
        IReadOnlyList<MovementProjection> movements,
        IReadOnlyList<LedgerProjection> ledgers,
        List<string> failures)
    {
        var recomputed = movements
            .GroupBy(x => x.DimensionKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity), StringComparer.Ordinal);

        foreach (var ledger in ledgers)
        {
            if (!recomputed.TryGetValue(ledger.DimensionKey, out var expected))
            {
                failures.Add($"台账 {ledger.DimensionKey} 上没有任何世界观流水，却存在结存 {ledger.OnHandQuantity}。");
                continue;
            }

            if (Math.Abs(ledger.OnHandQuantity - expected) > QuantityTolerance)
            {
                failures.Add(
                    $"台账 {ledger.DimensionKey} 现存量 {ledger.OnHandQuantity} 与流水代数和 {expected} 不平。");
            }
        }

        var ledgerKeys = ledgers.Select(x => x.DimensionKey).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in recomputed.Keys.Where(key => !ledgerKeys.Contains(key)).Take(5))
        {
            failures.Add($"流水维度 {missing} 上没有对应的台账。");
        }
    }

    /// <summary>2') 按时间回放：任何时刻的现存量都不得为负——这是「历史看起来能自洽」的底线。</summary>
    private static void CheckChronologicalReplay(
        IReadOnlyList<MovementProjection> movements,
        Dictionary<string, WorldHistoryStockMovementFact> factByKey,
        List<string> failures)
    {
        // 同刻流水按规格给出的生成序决胜（写入用的也是这个序），否则「解除持有 → 退回常驻库位」
        // 这类同刻链会被任意打乱，回放出假的负结存。
        var running = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var replayed = movements
            .OrderBy(x => x.PostedAtUtc)
            .ThenBy(x => factByKey.TryGetValue(x.Key, out var fact) ? fact.Sequence : int.MaxValue)
            .ThenBy(x => x.Key, StringComparer.Ordinal);
        foreach (var movement in replayed)
        {
            var next = running.GetValueOrDefault(movement.DimensionKey) + movement.Quantity;
            if (next < -QuantityTolerance)
            {
                failures.Add(
                    $"按时间回放到 {movement.Key}（{movement.PostedAtUtc:O}）时，台账 {movement.DimensionKey} 现存量变为 {next}。");
                if (failures.Count > 25)
                {
                    return;
                }
            }

            running[movement.DimensionKey] = next;
        }
    }

    /// <summary>3) 完工入库量 == MES 好品产出量；发货量 == 订单数量。</summary>
    private static void CheckProductionAndDeliveryChain(
        IReadOnlyList<WorldHistoryStockMovementFact> facts,
        IReadOnlyList<MovementProjection> movements,
        IReadOnlyList<WorldHistoryWorkOrderFact> workOrderFacts,
        DateOnly asOfDate,
        double scale,
        List<string> failures)
    {
        var byKey = movements.ToDictionary(x => x.Key, StringComparer.Ordinal);

        var workOrders = workOrderFacts
            .Where(fact => fact.HasFinishedGoodsReceipt)
            .ToDictionary(fact => fact.FinishedGoodsMovementId, fact => fact.Plan, StringComparer.Ordinal);
        foreach (var fact in facts.Where(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.FinishedGoodsInPurpose, StringComparison.Ordinal)))
        {
            if (!workOrders.TryGetValue(fact.IdempotencyKey, out var plan))
            {
                failures.Add($"完工入库流水 {fact.SourceDocumentId} 挂在一张没有完工入库的工单上。");
                continue;
            }

            if (byKey.TryGetValue(fact.MovementKey, out var movement) &&
                Math.Abs(movement.Quantity - plan.GoodQuantity) > QuantityTolerance)
            {
                failures.Add(
                    $"完工入库流水 {fact.SourceDocumentId} 入库 {movement.Quantity}，与工单好品产出 {plan.GoodQuantity} 不符。");
            }
        }

        var orders = WorldHistorySpec.BuildOrderPlans(asOfDate, scale)
            .Where(plan => plan.HasDelivery)
            .ToDictionary(plan => WorldHistorySpec.DeliveryOrderNo(plan.Index), StringComparer.Ordinal);
        foreach (var fact in facts.Where(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.DeliveryOutPurpose, StringComparison.Ordinal)))
        {
            if (!orders.TryGetValue(fact.SourceDocumentId, out var order))
            {
                failures.Add($"发货出库流水 {fact.SourceDocumentId} 挂在一张不发货的订单上。");
                continue;
            }

            if (byKey.TryGetValue(fact.MovementKey, out var movement) &&
                Math.Abs(-movement.Quantity - order.Quantity) > QuantityTolerance)
            {
                failures.Add(
                    $"发货出库流水 {fact.SourceDocumentId} 出库 {-movement.Quantity}，与订单数量 {order.Quantity} 不符。");
            }
        }
    }

    /// <summary>4) 报废调整合计必须为正，且不越过一期工单的投料放大量合计。</summary>
    private static void CheckScrapBoundary(
        IReadOnlyList<WorldHistoryStockMovementFact> facts,
        IReadOnlyList<MovementProjection> movements,
        IReadOnlyList<WorldHistoryWorkOrderFact> workOrderFacts,
        List<string> failures)
    {
        var scrapKeys = facts
            .Where(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.ScrapAdjustmentPurpose, StringComparison.Ordinal))
            .Select(x => x.MovementKey)
            .ToHashSet(StringComparer.Ordinal);
        if (scrapKeys.Count == 0)
        {
            return;
        }

        var scrapped = movements.Where(x => scrapKeys.Contains(x.Key)).Sum(x => -x.Quantity);
        var workOrderScrapTotal = workOrderFacts.Sum(fact => fact.Plan.ScrapQuantity);

        if (scrapped <= 0m)
        {
            failures.Add("本次历史应有报废调整，但库内报废数量合计为 0。");
        }

        if (scrapped > workOrderScrapTotal)
        {
            failures.Add($"报废调整数量合计 {scrapped} 越过一期工单投料放大量合计 {workOrderScrapTotal}。");
        }
    }

    /// <summary>6) 持有痕迹「施加 / 释放」成对：每一笔状态转出都有同量的状态转入。</summary>
    private static void CheckHoldPairs(
        IReadOnlyList<WorldHistoryStockMovementFact> facts,
        IReadOnlyList<MovementProjection> movements,
        List<string> failures)
    {
        var byKey = movements.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var outByPurpose = WorldHistoryInventorySpec.StatusTransferOutPurposes;
        var inByPurpose = WorldHistoryInventorySpec.StatusTransferInPurposes;

        for (var index = 0; index < outByPurpose.Length; index++)
        {
            var outPurpose = outByPurpose[index];
            var inPurpose = inByPurpose[index];
            var released = facts
                .Where(x => string.Equals(x.Purpose, inPurpose, StringComparison.Ordinal))
                .ToDictionary(x => x.SourceDocumentId, x => x.MovementKey, StringComparer.Ordinal);

            foreach (var applied in facts.Where(x => string.Equals(x.Purpose, outPurpose, StringComparison.Ordinal)))
            {
                if (!released.TryGetValue(applied.SourceDocumentId, out var pairedKey))
                {
                    failures.Add($"{applied.SourceDocumentId} 的 {outPurpose} 状态转移没有配对的 {inPurpose}。");
                    continue;
                }

                if (!byKey.TryGetValue(applied.MovementKey, out var appliedMovement) ||
                    !byKey.TryGetValue(pairedKey, out var releasedMovement))
                {
                    failures.Add($"{applied.SourceDocumentId} 的 {outPurpose}/{inPurpose} 状态转移未成对落库。");
                    continue;
                }

                if (Math.Abs(-appliedMovement.Quantity - releasedMovement.Quantity) > QuantityTolerance)
                {
                    failures.Add(
                        $"{applied.SourceDocumentId} 的状态转移数量不平：转出 {-appliedMovement.Quantity}，转入 {releasedMovement.Quantity}。");
                }

                if (releasedMovement.PostedAtUtc < appliedMovement.PostedAtUtc)
                {
                    failures.Add($"{applied.SourceDocumentId} 的 {inPurpose} 早于 {outPurpose}。");
                }
            }
        }
    }

    #endregion

    #region 载入紧凑投影

    private async Task<IReadOnlyList<MovementProjection>> LoadMovementsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await dbContext.StockMovements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.SourceService == WorldHistoryInventorySpec.SourceService)
            .Select(x => new MovementProjection(
                x.SourceDocumentId,
                x.IdempotencyKey,
                x.MovementType,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.QualityStatus,
                x.OwnerType,
                x.Quantity,
                x.PostedAtUtc))
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<LedgerProjection>> LoadLedgersAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.LotNo != null && x.LotNo.StartsWith("LOT-"))
            .Select(x => new LedgerProjection(
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.QualityStatus,
                x.OwnerType,
                x.OnHandQuantity))
            .ToArrayAsync(cancellationToken);

    #endregion

    private static bool IsPurpose(
        MovementProjection movement,
        Dictionary<string, WorldHistoryStockMovementFact> factByKey,
        string purpose) =>
        factByKey.TryGetValue(movement.Key, out var fact) &&
        string.Equals(fact.Purpose, purpose, StringComparison.Ordinal);

    private static IReadOnlyList<string> BuildSample(
        IReadOnlyList<MovementProjection> movements,
        Dictionary<string, WorldHistoryStockMovementFact> factByKey)
    {
        var ordered = movements
            .OrderBy(x => x.PostedAtUtc)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var stride = Math.Max(1, ordered.Length / SampleSize);
        var sample = new List<string>(SampleSize);
        for (var index = 0; index < ordered.Length && sample.Count < SampleSize; index += stride)
        {
            var movement = ordered[index];
            var purpose = factByKey.TryGetValue(movement.Key, out var fact) ? fact.Purpose : "unknown";
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture,
                $"{movement.PostedAtUtc:yyyy-MM-dd HH:mm}Z {movement.MovementType}/{purpose} {movement.SourceDocumentId}");
            builder.Append(CultureInfo.InvariantCulture,
                $" {movement.SkuCode}@{movement.LocationCode}[{movement.LotNo ?? "-"}/{movement.QualityStatus}]");
            builder.Append(CultureInfo.InvariantCulture, $" 数量={movement.Quantity:+0.##;-0.##}");
            sample.Add(builder.ToString());
        }

        return sample;
    }

    private sealed record MovementProjection(
        string SourceDocumentId,
        string IdempotencyKey,
        string MovementType,
        string SkuCode,
        string UomCode,
        string SiteCode,
        string LocationCode,
        string? LotNo,
        string QualityStatus,
        string OwnerType,
        decimal Quantity,
        DateTime PostedAtUtc)
    {
        public string Key => $"{SourceDocumentId}|{IdempotencyKey}";

        public string DimensionKey =>
            $"{SkuCode}|{UomCode}|{SiteCode}|{LocationCode}|{LotNo ?? "-"}|{QualityStatus}|{OwnerType}";
    }

    private sealed record LedgerProjection(
        string SkuCode,
        string UomCode,
        string SiteCode,
        string LocationCode,
        string? LotNo,
        string QualityStatus,
        string OwnerType,
        decimal OnHandQuantity)
    {
        public string DimensionKey =>
            $"{SkuCode}|{UomCode}|{SiteCode}|{LocationCode}|{LotNo ?? "-"}|{QualityStatus}|{OwnerType}";
    }
}

/// <summary>库存预留块（四期）的校验结论。</summary>
public sealed record WorldHistoryReservationValidationReport(
    int StockReservationsChecked,
    int OpenReservationsChecked,
    decimal ReservedQuantityTotal,
    decimal AvailableQuantityTotal,
    int LedgersWithReservationChecked,
    IReadOnlyList<string> Sample);

/// <summary>库存域侧一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryInventoryValidationReport(
    int StockMovementsChecked,
    int StockLedgersChecked,
    int DistinctLotsChecked,
    decimal OpeningQuantityTotal,
    decimal InboundQuantityTotal,
    decimal OutboundQuantityTotal,
    decimal ClosingQuantityTotal,
    IReadOnlyList<string> Sample);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
public sealed class WorldHistoryInventoryConsistencyException : InvalidOperationException
{
    public WorldHistoryInventoryConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryInventoryConsistencyException()
        : base("World-history inventory consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryInventoryConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryInventoryConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（库存域），共 ");
        builder.Append(failures.Count).AppendLine(" 条：");
        foreach (var failure in failures.Take(25))
        {
            builder.Append("  - ").AppendLine(failure);
        }

        if (failures.Count > 25)
        {
            builder.Append("  … 另有 ").Append(failures.Count - 25).AppendLine(" 条未列出。");
        }

        return builder.ToString();
    }
}
