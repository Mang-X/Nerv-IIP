using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **库存域侧**（二期）。
///
/// 产出（设定集 §7）：6 个世界观库位 + 期初建账 + 与一期 ERP/MES、二期质量域逐笔对应的移动流水
/// （采购收货 / 待检放行 / 上架 / 领料 / 线边倒冲 / 完工入库 / 发货 / 不合格品持有与报废），
/// 以及由这些流水推出来的现存量台账。
///
/// 与其余域的一致性靠 <see cref="WorldHistoryInventorySpec.BuildMovements"/> 一个确定性纯函数达成：
/// 源单据号全部来自一期已落库的采购收货单 / 领料单 / 完工入库请求 / 发货单与二期质量域的 NCR，
/// 两侧不通信、不跨库查询、不建跨 schema 外键。
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的
/// <c>StockLedger.ApplyMovement</c>，历史数据不会反向触发 CAP 集成事件风暴——与一期 seed 同一前提。
///
/// 写入顺序说明：流水**必须**按业务时间推进写入。<c>StockLedger.ApplyMovement</c> 会硬拒绝
/// 让现存量为负的流水，所以「先发货后完工」这类倒序不是数据难看，而是直接让 seed 失败。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批流水数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 500;

    private const string LocationStatus = "active";

    public async Task<WorldHistoryInventorySeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var locationsWritten = await SeedStockLocationsAsync(organizationId, environmentId, cancellationToken);
        var facts = WorldHistoryInventorySpec.BuildMovements(asOfDate, scale);

        var movementsWritten = 0;
        var ledgersCreated = 0;
        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var pending = await FilterPendingAsync(organizationId, environmentId, batch, cancellationToken);
            if (pending.Length == 0)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }

            var ledgers = await LoadLedgersAsync(organizationId, environmentId, pending, cancellationToken);
            foreach (var fact in pending)
            {
                if (!ledgers.TryGetValue(fact.DimensionKey, out var ledger))
                {
                    ledger = CreateLedger(organizationId, environmentId, fact);
                    dbContext.StockLedgers.Add(ledger);
                    ledgers[fact.DimensionKey] = ledger;
                    ledgersCreated++;
                }

                var movement = StockMovement.Post(
                    organizationId,
                    environmentId,
                    fact.MovementType,
                    WorldHistoryInventorySpec.SourceService,
                    fact.SourceDocumentId,
                    fact.SourceDocumentLineId,
                    fact.IdempotencyKey,
                    fact.SkuCode,
                    fact.UomCode,
                    WorldHistorySpec.SiteCode,
                    fact.LocationCode,
                    fact.LotNo,
                    serialNo: null,
                    fact.QualityStatus,
                    WorldHistoryInventorySpec.OwnerType,
                    ownerId: null,
                    fact.Quantity,
                    fact.UnitCost);
                // ApplyMovement 命中批内重复键时会回传既有流水；只有真正新建的才入库（与命令管线同一姿势）。
                if (!ReferenceEquals(ledger.ApplyMovement(movement), movement))
                {
                    continue;
                }

                dbContext.StockMovements.Add(movement);

                // 过账时刻与台账最后变更时刻都要回填，否则历史页面会显示成「今天刚发生」。
                Backdate(movement, x => x.PostedAtUtc, fact.PostedAtUtc.UtcDateTime);
                Backdate(ledger, x => x.UpdatedAtUtc, fact.PostedAtUtc.UtcDateTime);
                movementsWritten++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        // fail-closed：现存量恒等式 / 数量链 / 持有痕迹成对对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryInventorySeedReport(
            StockLocationsWritten: locationsWritten,
            StockMovementsWritten: movementsWritten,
            StockLedgersCreated: ledgersCreated,
            Validation: validation);
    }

    #region 库位

    /// <summary>按 <c>LocationCode</c> 幂等补齐 6 个世界观库位；已存在的一律不动（保留租户事实）。</summary>
    private async Task<int> SeedStockLocationsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var codes = WorldHistoryPhase2Spec.StockLocations.Select(x => x.LocationCode).ToArray();
        var existing = (await dbContext.StockLocations
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    codes.Contains(x.LocationCode))
                .Select(x => x.LocationCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // 库位没有业务时间戳语义，统一回填到上线日，避免历史页面里出现「今天建的历史库位」。
        var goLiveUtc = WorldHistoryCalendar.GoLiveDate.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc);
        var written = 0;
        foreach (var definition in WorldHistoryPhase2Spec.StockLocations.Where(x => !existing.Contains(x.LocationCode)))
        {
            var location = StockLocation.CreateOrUpdate(
                existing: null,
                organizationId,
                environmentId,
                definition.LocationCode,
                definition.LocationType,
                WorldHistorySpec.SiteCode,
                parentLocationCode: null,
                LocationStatus);
            dbContext.StockLocations.Add(location);
            Backdate(location, x => x.CreatedAtUtc, goLiveUtc);
            Backdate(location, x => x.UpdatedAtUtc, goLiveUtc);
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    #endregion

    #region 批内预查

    /// <summary>剔除本批里已经落过库的流水（幂等重跑时整批命中，写 0 条）。</summary>
    private async Task<WorldHistoryStockMovementFact[]> FilterPendingAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryStockMovementFact> batch,
        CancellationToken cancellationToken)
    {
        var documentIds = batch.Select(x => x.SourceDocumentId).Distinct(StringComparer.Ordinal).ToArray();
        var keys = batch.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).ToArray();
        var existing = (await dbContext.StockMovements
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.SourceService == WorldHistoryInventorySpec.SourceService &&
                    documentIds.Contains(x.SourceDocumentId) && keys.Contains(x.IdempotencyKey))
                .Select(x => new { x.SourceDocumentId, x.IdempotencyKey })
                .ToArrayAsync(cancellationToken))
            .Select(x => $"{x.SourceDocumentId}|{x.IdempotencyKey}")
            .ToHashSet(StringComparer.Ordinal);

        return [.. batch.Where(fact => !existing.Contains(fact.MovementKey))];
    }

    /// <summary>
    /// 载入本批涉及的台账维度。EF 无法对「维度元组集合」直接生成 IN 查询，
    /// 因此按 SKU / 库位 / 批次三个高选择度列粗筛，再在内存里按完整维度键精配。
    /// </summary>
    private async Task<Dictionary<string, StockLedger>> LoadLedgersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryStockMovementFact> batch,
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

    private static StockLedger CreateLedger(
        string organizationId,
        string environmentId,
        WorldHistoryStockMovementFact fact) =>
        StockLedger.Create(
            organizationId,
            environmentId,
            fact.SkuCode,
            fact.UomCode,
            WorldHistorySpec.SiteCode,
            fact.LocationCode,
            fact.LotNo,
            serialNo: null,
            fact.QualityStatus,
            WorldHistoryInventorySpec.OwnerType,
            ownerId: null);

    #endregion

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }
}

/// <summary>一次 L1 库存域历史生成的产出摘要。</summary>
public sealed record WorldHistoryInventorySeedReport(
    int StockLocationsWritten,
    int StockMovementsWritten,
    int StockLedgersCreated,
    WorldHistoryInventoryValidationReport Validation);
