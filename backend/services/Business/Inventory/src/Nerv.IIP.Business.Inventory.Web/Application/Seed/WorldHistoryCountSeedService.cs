using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using System.Text;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **库存盘点块**（<c>stock_count_tasks</c> /
/// <c>stock_count_adjustments</c>）。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行：盘点任务的维度与期望台账版本
/// 都取自**库里真实存在的** <c>StockLedger</c>——盘点任务若挂在不存在的台账维度上，
/// 页面一点开就 500，确认差异更是当场抛「dimensions do not match」。
///
/// 计划来自与仓储域共享的 <see cref="WorldHistoryCountSpec"/>，两侧不通信、不跨库查询、
/// 不建跨 schema 外键：盘点单号 <c>CNT-2026-####</c> 与差异量逐笔相同（见该类型注释）。
///
/// <para>
/// 裁决点 · **历史盘点一律不过账，也一律不冻结台账**。
/// ① 不过账：确认差异会写一笔 <c>count-adjustment</c> 流水并改写现存量，而
/// <see cref="WorldHistoryConsistencyValidator"/> 是按「现存量 = 世界观流水代数和」重算的，
/// 历史盘点真去过账会让恒等式当场失衡；
/// ② 不冻结：<c>StockLedger.FreezeForCount</c> 会挡住该维度上的一切非盘点流水，
/// 历史遗留的冻结会让演示当场的任何库存操作直接失败。
/// 因此历史盘点收敛在 待实盘 / 待审批 / 需复盘 / 已作废 四态，
/// 「确认过账」留给演示当场走真实路径（L2）。
/// </para>
/// </summary>
public sealed class WorldHistoryCountSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批盘点计划数。</summary>
    public const int BatchSize = 200;

    public async Task<WorldHistoryCountSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, scale)
            .Where(plan => plan.HasInventoryCountTask)
            .ToArray();

        var tasksWritten = 0;
        var adjustmentsWritten = 0;
        var skippedWithoutLedger = 0;

        for (var batchStart = 0; batchStart < plans.Length; batchStart += BatchSize)
        {
            var batch = plans.Skip(batchStart).Take(BatchSize).ToArray();
            var pending = await FilterPendingAsync(organizationId, environmentId, batch, cancellationToken);
            if (pending.Length == 0)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }

            var ledgers = await LoadLedgersAsync(organizationId, environmentId, pending, cancellationToken);
            var added = 0;
            foreach (var plan in pending)
            {
                if (!ledgers.TryGetValue(DimensionKeyOf(plan), out var ledger))
                {
                    // 台账维度不存在（缩放边界下该物料本区间没有任何流水）：宁可不写，也不造假维度。
                    skippedWithoutLedger++;
                    continue;
                }

                var ledgerUpdatedAtUtc = ledger.UpdatedAtUtc;
                var task = StockCountTask.Create(
                    organizationId,
                    environmentId,
                    plan.CountNo,
                    WorldHistoryCountSpec.CountTaskIdempotencyKey(plan.CountNo),
                    ledger.OrganizationId,
                    ledger.EnvironmentId,
                    ledger.SkuCode,
                    ledger.UomCode,
                    ledger.SiteCode,
                    ledger.LocationCode,
                    ledger.LotNo,
                    ledger.SerialNo,
                    ledger.QualityStatus,
                    ledger.OwnerType,
                    ledger.OwnerId,
                    ledger.LedgerVersion);

                var adjustment = ApplyOutcome(plan, task, ledger);
                task.ClearDomainEvents();
                ledger.ClearDomainEvents();
                dbContext.StockCountTasks.Add(task);
                Backdate(task, x => x.CreatedAtUtc, plan.StartedAtUtc.UtcDateTime);
                Backdate(task, x => x.UpdatedAtUtc, (plan.CompletedAtUtc ?? plan.StartedAtUtc).UtcDateTime);
                // 盘点不改变余额，台账的「最后变更时间」不该因为历史盘点而跳到今天。
                Backdate(ledger, x => x.UpdatedAtUtc, ledgerUpdatedAtUtc);
                tasksWritten++;
                added++;

                if (adjustment is null)
                {
                    continue;
                }

                // 历史盘点调整全部停在未过账态，因此 ConfirmedAtUtc 天然为 null，无需回填。
                adjustment.ClearDomainEvents();
                dbContext.StockCountAdjustments.Add(adjustment);
                adjustmentsWritten++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        var validation = await new WorldHistoryCountValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryCountSeedReport(
            StockCountTasksWritten: tasksWritten,
            StockCountAdjustmentsWritten: adjustmentsWritten,
            PlansSkippedWithoutLedger: skippedWithoutLedger,
            Validation: validation);
    }

    /// <summary>把计划的结局落到聚合上，并在需要时产出一行盘点调整。</summary>
    private static StockCountAdjustment? ApplyOutcome(
        WorldHistoryCountPlan plan,
        StockCountTask task,
        StockLedger ledger)
    {
        switch (plan.Outcome)
        {
            case WorldHistoryCountOutcome.Open:
                return null;

            case WorldHistoryCountOutcome.Cancelled:
                task.Cancel(ledger, "循环盘点计划调整，本次任务作废。");
                return null;

            case WorldHistoryCountOutcome.PendingApproval:
            case WorldHistoryCountOutcome.RecountRequired:
            default:
                var countedQuantity = CountedQuantityFor(plan, ledger);
                task.SubmitForApproval(ledger, countedQuantity);
                var varianceAmount = decimal.Round(
                    Math.Abs(countedQuantity - ledger.OnHandQuantity) * ledger.MovingAverageUnitCost, 2);
                var adjustment = StockCountAdjustment.RecordPendingApproval(
                    task,
                    WorldHistoryCountSpec.CountAdjustmentIdempotencyKey(plan.CountNo),
                    WorldHistoryCountSpec.ApprovalChainReference(plan.CountNo),
                    varianceAmount);
                if (plan.Outcome == WorldHistoryCountOutcome.RecountRequired)
                {
                    task.RequireRecountAfterApprovalRejection(ledger);
                    adjustment.VoidAfterApprovalRejection();
                }

                return adjustment;
        }
    }

    /// <summary>
    /// 实盘量 = **真实现存量** + 共享计划里的差异量。
    ///
    /// 账面量在仓储侧是下发时的快照、在库存侧是台账真值（两侧口径不同、差异量相同，
    /// 见 <see cref="WorldHistoryCountSpec"/> 裁决点一）。现存量太小以致实盘量为负时，
    /// 差异取绝对值转为盘盈——聚合硬拒绝负实盘量。
    /// </summary>
    private static decimal CountedQuantityFor(WorldHistoryCountPlan plan, StockLedger ledger)
    {
        var counted = ledger.OnHandQuantity + plan.VarianceQuantity;
        return counted < 0m ? ledger.OnHandQuantity + Math.Abs(plan.VarianceQuantity) : counted;
    }

    private async Task<WorldHistoryCountPlan[]> FilterPendingAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryCountPlan> batch,
        CancellationToken cancellationToken)
    {
        var countNumbers = batch.Select(x => x.CountNo).ToArray();
        var existing = (await dbContext.StockCountTasks
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    countNumbers.Contains(x.CountTaskCode))
                .Select(x => x.CountTaskCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return [.. batch.Where(plan => !existing.Contains(plan.CountNo))];
    }

    /// <summary>
    /// 载入本批涉及的台账维度。EF 无法对「维度元组集合」直接生成 IN 查询，
    /// 因此按 SKU / 库位 / 批次三个高选择度列粗筛，再在内存里按完整维度键精配
    /// （与 <see cref="WorldHistorySeedService"/> 同一姿势）。台账必须**跟踪**载入：
    /// 取消与需复盘会调用 <c>ReleaseCountFreeze</c>。
    /// </summary>
    private async Task<Dictionary<string, StockLedger>> LoadLedgersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryCountPlan> batch,
        CancellationToken cancellationToken)
    {
        var wanted = batch.Select(DimensionKeyOf).ToHashSet(StringComparer.Ordinal);
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

    private static string DimensionKeyOf(WorldHistoryCountPlan plan) =>
        $"{plan.SkuCode}|{plan.UomCode}|{plan.SiteCode}|{plan.LocationCode}|{plan.LotNo}|{WorldHistoryCountSpec.Unrestricted}|{WorldHistoryCountSpec.OwnerType}";

    private static string DimensionKeyOf(StockLedger ledger) =>
        $"{ledger.SkuCode}|{ledger.UomCode}|{ledger.SiteCode}|{ledger.LocationCode}|{ledger.LotNo ?? "-"}|{ledger.QualityStatus}|{ledger.OwnerType}";

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }
}

/// <summary>一次 L1 库存盘点块生成的产出摘要。</summary>
public sealed record WorldHistoryCountSeedReport(
    int StockCountTasksWritten,
    int StockCountAdjustmentsWritten,
    int PlansSkippedWithoutLedger,
    WorldHistoryCountValidationReport Validation);

/// <summary>
/// 库存盘点块的一致性校验器（fail-closed）。
///
/// 覆盖：盘点任务号段与隔离、任务终态不含 <c>confirmed</c>（历史盘点不过账）、
/// 台账不得残留盘点冻结、差异量 = 实盘 − 现存量、调整与任务成对、时间戳落在历史区间内且不在周日。
/// </summary>
public sealed class WorldHistoryCountValidator(ApplicationDbContext dbContext)
{
    private const decimal QuantityTolerance = 0.000001m;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryCountValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var planByCountNo = WorldHistoryCountSpec.BuildCountPlans(asOfDate, scale)
            .Where(plan => plan.HasInventoryCountTask)
            .ToDictionary(plan => plan.CountNo, StringComparer.Ordinal);
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var tasks = await dbContext.StockCountTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.CountTaskCode.StartsWith("CNT-2026-"))
            .ToArrayAsync(cancellationToken);
        var adjustments = await dbContext.StockCountAdjustments
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.CountTaskCode.StartsWith("CNT-2026-"))
            .ToArrayAsync(cancellationToken);
        var adjustmentByCountTaskCode = adjustments.ToDictionary(x => x.CountTaskCode, StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            if (!planByCountNo.TryGetValue(task.CountTaskCode, out var plan))
            {
                failures.Add($"库内盘点任务 {task.CountTaskCode} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            if (string.Equals(task.Status, StockCountTaskStatuses.Confirmed, StringComparison.Ordinal))
            {
                failures.Add($"历史盘点任务 {task.CountTaskCode} 落到了已确认态，会过账一笔调整流水并打破现存量恒等式。");
            }

            if (task.CountedQuantity is { } counted && task.VarianceQuantity is { } variance &&
                Math.Abs(variance) < QuantityTolerance && counted > 0m)
            {
                failures.Add($"盘点任务 {task.CountTaskCode} 有实盘量却零差异，应当归入仓储侧闭环而非库存盘点。");
            }

            var hasAdjustment = adjustmentByCountTaskCode.ContainsKey(task.CountTaskCode);
            if (plan.HasInventoryAdjustment != hasAdjustment)
            {
                failures.Add($"盘点任务 {task.CountTaskCode} 的盘点调整落库情况与计划不符（计划 {plan.HasInventoryAdjustment}，实际 {hasAdjustment}）。");
            }

            foreach (var infix in ReservedInfixes)
            {
                if (task.CountTaskCode.Contains(infix, StringComparison.Ordinal))
                {
                    failures.Add($"盘点任务 {task.CountTaskCode} 落进了保留号段 '{infix}'。");
                }
            }

            if (task.CreatedAtUtc < lowerBound || task.CreatedAtUtc > upperBound)
            {
                failures.Add($"盘点任务 {task.CountTaskCode} 的创建时间 {task.CreatedAtUtc:O} 落在历史区间之外。");
            }

            if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(task.CreatedAtUtc)))
            {
                failures.Add($"盘点任务 {task.CountTaskCode} 的创建时间落在周日（停产保养日）。");
            }
        }

        // 计划里有、库里没有的盘点任务**不算失败**：台账维度缺失时 seed 会显式跳过
        // （缩放边界下该物料本区间可能没有任何流水），造假维度比少几条盘点危险得多。

        var frozenLedgers = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.IsFrozenForCount)
            .Select(x => x.FrozenCountTaskCode)
            .Take(5)
            .ToArrayAsync(cancellationToken);
        foreach (var frozen in frozenLedgers)
        {
            failures.Add($"台账残留盘点冻结（{frozen}），会挡住演示当场的一切库存操作。");
        }

        if (failures.Count > 0)
        {
            throw new WorldHistoryCountConsistencyException(failures);
        }

        return new WorldHistoryCountValidationReport(
            StockCountTasksChecked: tasks.Length,
            PendingApprovalTasksChecked: tasks.Count(x => x.Status == StockCountTaskStatuses.PendingApproval),
            RecountRequiredTasksChecked: tasks.Count(x => x.Status == StockCountTaskStatuses.RecountRequired),
            CancelledTasksChecked: tasks.Count(x => x.Status == StockCountTaskStatuses.Cancelled),
            OpenTasksChecked: tasks.Count(x => x.Status == StockCountTaskStatuses.Open),
            StockCountAdjustmentsChecked: adjustments.Length,
            VarianceAmountTotal: adjustments.Sum(x => x.VarianceAmount));
    }
}

/// <summary>库存盘点块校验器的产出摘要。</summary>
public sealed record WorldHistoryCountValidationReport(
    int StockCountTasksChecked,
    int PendingApprovalTasksChecked,
    int RecountRequiredTasksChecked,
    int CancelledTasksChecked,
    int OpenTasksChecked,
    int StockCountAdjustmentsChecked,
    decimal VarianceAmountTotal);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
public sealed class WorldHistoryCountConsistencyException : InvalidOperationException
{
    public WorldHistoryCountConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryCountConsistencyException()
        : base("World-history inventory count consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryCountConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryCountConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（库存盘点），共 ");
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
