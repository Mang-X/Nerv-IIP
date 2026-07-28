using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **库存预留块**（<c>stock_reservations</c>）。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行：预留的维度直接取自**库里真实存在的**
/// <c>StockLedger</c>（<c>StockReservation.Reserve</c> 的工厂签名就要求一条台账），
/// 预留挂在不存在的维度上等于一条指向空台账的死行。
///
/// 计划来自 <see cref="WorldHistoryReservationSpec"/>（确定性纯函数，seed 与校验器共用）。
///
/// <para><b>恒等式红线的落地（三条，逐条对应校验器条款）</b>：
/// <list type="number">
/// <item><b>只动 <c>ReservedQuantity</c> 与 <c>LedgerVersion</c></b>：本服务只调
///       <c>StockLedger.Reserve</c>——领域侧它只加 <c>ReservedQuantity</c> 并 <c>LedgerVersion++</c>，
///       <c>OnHandQuantity</c> 一个字节都不碰；</item>
/// <item><b>一笔流水都不写</b>：本服务只 <c>Add</c> <c>StockReservations</c>，
///       从不构造 <c>StockMovement</c>。预留不是移动；写流水会让
///       「现存量 = 世界观流水代数和」当场失衡（WMS 那批已踩过，见提交 <c>e4deae3</c>）；</item>
/// <item><b>已释放的历史预留不回放到台账</b>：净效应为零，且成品批此刻已归零，
///       回放 <c>Reserve</c> 会被「预留超过可用量」拒绝（那是对**今天**做的检查）。
///       故家族一只落预留行，家族二才真正占用（裁决点见 <see cref="WorldHistoryReservationSpec"/>）。</item>
/// </list></para>
///
/// <para>领域事件：<c>StockLedger.Reserve</c> 会 <c>AddDomainEvent(StockAvailabilityChangedDomainEvent)</c>，
/// 该事件有跨服务转换器（→ <c>StockAvailabilityChangedIntegrationEvent</c>）。本仓栈里
/// <c>SaveChangesAsync</c> 不派发领域事件，但仍显式 <c>ClearDomainEvents()</c>——
/// 历史事实不驱动下游（与 Maintenance 备件块同一姿势）。</para>
/// </summary>
public sealed class WorldHistoryReservationSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批预留数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 500;

    public async Task<WorldHistoryReservationSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var plans = WorldHistoryReservationSpec.BuildReservations(asOfDate, scale);
        var expiresAtUtc = WorldHistoryReservationSpec.OpenReservationExpiresAtUtc(asOfDate);

        var written = 0;
        var openWritten = 0;
        var skippedWithoutLedger = 0;
        var skippedNotKitted = 0;

        for (var batchStart = 0; batchStart < plans.Count; batchStart += BatchSize)
        {
            var batch = plans.Skip(batchStart).Take(BatchSize).ToArray();
            var pending = await FilterPendingAsync(organizationId, environmentId, batch, cancellationToken);
            if (pending.Length == 0)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }

            var ledgers = await LoadLedgersAsync(organizationId, environmentId, pending, cancellationToken);
            foreach (var plan in pending)
            {
                if (!ledgers.TryGetValue(plan.DimensionKey, out var ledger))
                {
                    // 台账维度不存在（缩放边界下该物料本区间没有任何流水）：宁可不写，也不造假维度。
                    skippedWithoutLedger++;
                    continue;
                }

                // 齐套预留只在库存真的够时才落——这不是为了绕开异常，而是「齐套检查」的本义：
                // 不齐套就预留不上。短历史 / 小缩放下期初余量本就吃紧（期初 = 耗用量 × 1.2），
                // 硬占会被 Reserve 拒绝（「预留超过可用量」），而把数量截短则是在造假账。
                if (plan.IsOpen && plan.Quantity > ledger.AvailableQuantity)
                {
                    skippedNotKitted++;
                    continue;
                }

                var reservation = StockReservation.Reserve(
                    ledger,
                    WorldHistoryReservationSpec.SourceService,
                    plan.SourceDocumentId,
                    plan.SourceDocumentLineId,
                    plan.IdempotencyKey,
                    plan.Quantity,
                    // 失效时刻恒被构造函数要求「在未来」；一律先取默认值，再按计划回填/前推，
                    // 于是生成结果与运行时钟无关（历史引擎必须可复现）。
                    expiresAtUtc: null);

                if (plan.IsOpen)
                {
                    // 家族二：真正占用库存。Reserve 会在「预留 > 可用」时抛——这正是我们要的 fail-closed。
                    var ledgerUpdatedAtUtc = ledger.UpdatedAtUtc;
                    ledger.Reserve(reservation);
                    ledger.ClearDomainEvents();
                    Backdate(ledger, x => x.UpdatedAtUtc,
                        Later(ledgerUpdatedAtUtc, plan.CreatedAtUtc.UtcDateTime));
                    openWritten++;
                }
                else
                {
                    // 家族一：历史上占用过、发货时已释放，净效应为零，不回放到台账。
                    reservation.Release(plan.Quantity);
                }

                dbContext.StockReservations.Add(reservation);
                Backdate(reservation, x => x.CreatedAtUtc, plan.CreatedAtUtc.UtcDateTime);
                Backdate(reservation, x => x.UpdatedAtUtc, (plan.ReleasedAtUtc ?? plan.CreatedAtUtc).UtcDateTime);
                Backdate(reservation, x => x.ExpiresAtUtc, plan.IsOpen
                    ? expiresAtUtc.UtcDateTime
                    : plan.CreatedAtUtc.UtcDateTime.AddHours(4));
                written++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        // fail-closed：预留对账、现存量恒等式未被扰动、预留没有产生任何流水——任一不成立就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateReservationsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryReservationSeedReport(
            StockReservationsWritten: written,
            OpenReservationsWritten: openWritten,
            PlansSkippedWithoutLedger: skippedWithoutLedger,
            PlansSkippedNotKitted: skippedNotKitted,
            Validation: validation);
    }

    /// <summary>剔除本批里已经落过库的预留（幂等重跑时整批命中，写 0 条）。</summary>
    private async Task<WorldHistoryReservationPlan[]> FilterPendingAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryReservationPlan> batch,
        CancellationToken cancellationToken)
    {
        var documentIds = batch.Select(x => x.SourceDocumentId).Distinct(StringComparer.Ordinal).ToArray();
        var keys = batch.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).ToArray();
        var existing = (await dbContext.StockReservations
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.SourceService == WorldHistoryReservationSpec.SourceService &&
                    documentIds.Contains(x.SourceDocumentId) && keys.Contains(x.IdempotencyKey))
                .Select(x => new { x.SourceDocumentId, x.IdempotencyKey })
                .ToArrayAsync(cancellationToken))
            .Select(x => $"{x.SourceDocumentId}|{x.IdempotencyKey}")
            .ToHashSet(StringComparer.Ordinal);

        return [.. batch.Where(plan => !existing.Contains(plan.ReservationKey))];
    }

    /// <summary>
    /// 载入本批涉及的台账维度（与 <see cref="WorldHistorySeedService"/> 同一姿势：
    /// 按 SKU / 库位 / 批次三个高选择度列粗筛，再在内存里按完整维度键精配）。
    /// </summary>
    private async Task<Dictionary<string, StockLedger>> LoadLedgersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryReservationPlan> batch,
        CancellationToken cancellationToken)
    {
        var wanted = batch.Select(x => x.DimensionKey).ToHashSet(StringComparer.Ordinal);
        var skuCodes = batch.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).ToArray();
        var locationCodes = batch.Select(x => x.LocationCode).Distinct(StringComparer.Ordinal).ToArray();
        var lotNumbers = batch.Select(x => x.LotNo).Distinct(StringComparer.Ordinal).ToArray();

        var candidates = await dbContext.StockLedgers
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                skuCodes.Contains(x.SkuCode) && locationCodes.Contains(x.LocationCode) &&
                lotNumbers.Contains(x.LotNo))
            .ToArrayAsync(cancellationToken);

        var ledgers = new Dictionary<string, StockLedger>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = DimensionKeyOf(candidate);
            if (wanted.Contains(key))
            {
                ledgers[key] = candidate;
            }
        }

        return ledgers;
    }

    private static string DimensionKeyOf(StockLedger ledger) =>
        $"{ledger.SkuCode}|{ledger.UomCode}|{ledger.SiteCode}|{ledger.LocationCode}|{ledger.LotNo ?? "-"}|{ledger.QualityStatus}|{ledger.OwnerType}";

    private static DateTime Later(DateTime candidate, DateTime floor) => candidate > floor ? candidate : floor;

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }
}

/// <summary>一次 L1 库存预留历史生成的产出摘要。</summary>
public sealed record WorldHistoryReservationSeedReport(
    int StockReservationsWritten,
    int OpenReservationsWritten,
    int PlansSkippedWithoutLedger,
    int PlansSkippedNotKitted,
    WorldHistoryReservationValidationReport Validation);
