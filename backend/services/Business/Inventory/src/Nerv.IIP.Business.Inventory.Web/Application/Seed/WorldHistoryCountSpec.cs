namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的 **循环盘点计划**——仓储域「盘点执行」与库存域
/// 「盘点任务 / 盘点调整」共享的确定性纯函数。
///
/// 两个服务不通信、不跨库查询、不建跨 schema 外键：它们用同一 <c>(asOfDate, scale)</c>
/// 调用 <see cref="BuildCountPlans"/>，得到逐字段相同的盘点计划表，于是
/// <list type="bullet">
/// <item>仓储的 <c>count_executions.count_no</c> 与库存的 <c>stock_count_tasks.count_task_code</c>
///       是**同一个** <c>CNT-2026-####</c>；</item>
/// <item>两侧的**差异量** <see cref="WorldHistoryCountPlan.VarianceQuantity"/> 逐笔相同；</item>
/// <item>两侧的物料 / 单位 / 工厂 / 库位 / 批次维度逐字段相同。</item>
/// </list>
///
/// <para>
/// 裁决点一 · **账面量两侧口径不同，差异量两侧口径相同**。仓储是执行侧，它的
/// <see cref="WorldHistoryCountPlan.ExpectedQuantity"/> 是**下发盘点单时的账面快照**（本规格给出的
/// 确定性数）；库存是账侧，它的差异要对着**真实的 <c>StockLedger.OnHandQuantity</c>** 算，
/// 否则「实盘 − 账面 = 差异」在库存页面上会自相矛盾。因此两侧共享的是差异量而不是账面量，
/// 库存侧的实盘量 = 真实现存量 + 本表差异量。这是一处**有意的、可解释的**非对称。
/// </para>
/// <para>
/// 裁决点二 · **零差异盘点不进库存域**。<c>StockMovement</c> 硬拒绝数量为 0 的流水，
/// 零差异盘点本来也没有可过账的调整；真实系统里这类盘点在仓储侧闭环即可。
/// 因此只有 <see cref="WorldHistoryCountPlan.HasInventoryCountTask"/> 为真（有差异或未完成）
/// 的盘点才会在库存域产生 <c>stock_count_tasks</c> 行。
/// </para>
/// <para>
/// 裁决点三 · **历史盘点一律不落到 <c>confirmed</c>**。确认差异会过账一笔调整流水并改写现存量，
/// 而库存域的一致性校验器是按「现存量 = 世界观流水代数和」重算的——历史盘点若真去过账，
/// 恒等式会当场失衡。历史盘点因此收敛在 待实盘 / 待审批 / 需复盘 / 已作废 四态，
/// 「确认过账」留给演示当场操作（L2）。
/// </para>
///
/// 仓储与库存两侧按同一字面量重复声明本类型，各有黄金向量测试防止漂移
/// （与 <see cref="WorldHistoryPhase2Spec"/> 的策略一致）。
/// </summary>
public static class WorldHistoryCountSpec
{
    /// <summary>盘点单号（设定集 §9 二期补登记段）。</summary>
    public static string CountNo(int ordinal) => $"CNT-2026-{ordinal:D4}";

    /// <summary>盘点差异的审批链代理号：跨库拿不到审批服务的 GUID，用确定性代理号记录。</summary>
    public static string ApprovalChainReference(string countNo) => $"APPR-{countNo}";

    /// <summary>库存侧盘点任务的幂等键。</summary>
    public static string CountTaskIdempotencyKey(string countNo) => $"{countNo}:count-task";

    /// <summary>库存侧盘点调整的幂等键。</summary>
    public static string CountAdjustmentIdempotencyKey(string countNo) => $"{countNo}:count-adjustment";

    /// <summary>本计划产出的号段前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["CNT-2026-", "APPR-CNT-2026-"];

    /// <summary>循环盘点每次覆盖的库位 / 物料组合数（设定集 §7：仓储按周做循环盘点）。</summary>
    public const int CountsPerCountDay = 6;

    /// <summary>盘点固定排在每周三（周日停产保养，春节停线两周不排盘点）。</summary>
    public const DayOfWeek CountDayOfWeek = DayOfWeek.Wednesday;

    public const string OwnerType = "company";
    public const string Unrestricted = "unrestricted";

    /// <summary>作业时长带宽（分钟）：一次循环盘点 60–180 分钟。</summary>
    private const int MinimumDurationMinutes = 60;
    private const int MaximumDurationMinutes = 180;

    #region 盘点维度（只盘有期初批的物料——那正是库存台账上恒定存在的维度）

    /// <summary>
    /// 可盘维度：全部成品的主料去重（<c>SF-ROD/TUB/VLV-*</c> 与 <c>RM-SPR-*</c>，共 22 条），
    /// 各自落在常驻库位的期初批 <c>LOT-OPENING-{物料}</c> 上。
    ///
    /// 成品不进盘点计划：成品台账按产出批 <c>LOT-{工单}</c> 一单一批，
    /// 循环盘点盘的是常备库存，不是逐张工单的产出批。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryCountDimension> Dimensions = BuildDimensions();

    private static IReadOnlyList<WorldHistoryCountDimension> BuildDimensions()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dimensions = new List<WorldHistoryCountDimension>(32);
        foreach (var component in WorldHistorySpec.FinishedGoodSkus.SelectMany(WorldHistoryMesSpec.Components))
        {
            if (!seen.Add(component.SkuCode))
            {
                continue;
            }

            dimensions.Add(new WorldHistoryCountDimension(
                component.SkuCode,
                component.UomCode,
                WorldHistorySpec.SiteCode,
                WorldHistoryPhase2Spec.StorageLocationFor(component.SkuCode),
                OpeningLotNo(component.SkuCode)));
        }

        return [.. dimensions.OrderBy(x => x.SkuCode, StringComparer.Ordinal)];
    }

    /// <summary>期初批次号：与库存域 <c>WorldHistoryInventorySpec.OpeningLotNo</c> 同字面量。</summary>
    public static string OpeningLotNo(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        return $"LOT-OPENING-{skuCode}";
    }

    #endregion

    /// <summary>盘点日：上线日之后的每个周三（跳过周日与春节停线）。</summary>
    public static IReadOnlyList<DateOnly> CountDays(DateOnly asOfDate)
    {
        var upperBound = asOfDate < WorldHistoryCalendar.GoLiveDate ? WorldHistoryCalendar.GoLiveDate : asOfDate;
        var cursor = WorldHistoryCalendar.GoLiveDate;
        while (cursor.DayOfWeek != CountDayOfWeek)
        {
            cursor = cursor.AddDays(1);
        }

        var days = new List<DateOnly>(64);
        while (cursor <= upperBound)
        {
            if (WorldHistoryCalendar.IsWorkingDay(cursor) && !WorldHistoryCalendar.IsSpringFestival(cursor))
            {
                days.Add(cursor);
            }

            cursor = cursor.AddDays(7);
        }

        // 上线当周还没走到第一个周三时，把上线日本身当作首次盘点日——否则边界日会一条都不生成。
        return days.Count == 0 ? [WorldHistoryCalendar.GoLiveDate] : days;
    }

    /// <summary>本区间的盘点条数（缩放后）。</summary>
    public static int CountPlanCount(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var slots = CountDays(asOfDate).Count * CountsPerCountDay;
        return Math.Max(1, (int)Math.Round(slots * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>「最近一批尚未下发实盘」的条数：页面的「进行中盘点」靠它非空（作废的另计）。</summary>
    public static int OpenCountPlanCount(DateOnly asOfDate, double scale)
    {
        var total = CountPlanCount(asOfDate, scale);
        return Math.Min(CountsPerCountDay, Math.Max(1, total / 20));
    }

    /// <summary>
    /// 全量盘点计划，按盘点日升序、日内按槽位升序。
    ///
    /// 结局分布用**配额分层**而不是概率抽样：概率抽样在小 scale 下会整档缺席，
    /// 演示时「待审批」「需复盘」页签会莫名其妙全空（教训见 ECO 状态分层）。
    /// </summary>
    public static IReadOnlyList<WorldHistoryCountPlan> BuildCountPlans(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var days = CountDays(asOfDate);
        var total = CountPlanCount(asOfDate, scale);
        var openTail = OpenCountPlanCount(asOfDate, scale);
        var plans = new List<WorldHistoryCountPlan>(total);

        for (var index = 0; index < total; index++)
        {
            var ordinal = index + 1;
            var countNo = CountNo(ordinal);
            var day = days[(index / CountsPerCountDay) % days.Count];
            var random = new WorldHistoryRandom($"count-plan:{countNo}");
            var dimension = random.Pick(Dimensions);
            var expectedQuantity = random.NextQuantity(200, 4000, 50);

            var outcome = index >= total - openTail
                ? WorldHistoryCountOutcome.Open
                : ResolveOutcome(index);
            // 只有走到审批的盘点才有差异：账实相符是 0，作废与在盘的从未实盘、同样是 0。
            var variance = outcome is WorldHistoryCountOutcome.PendingApproval or WorldHistoryCountOutcome.RecountRequired
                ? (random.Chance(0.45d) ? -1m : 1m) * random.NextInt(1, 9)
                : 0m;

            var startedAtUtc = WorldHistoryPhase2Spec.MomentOn(day, countNo, "warehouse-count");
            var completedAtUtc = IsCompletedOutcome(outcome)
                ? startedAtUtc.AddMinutes(random.NextInt(MinimumDurationMinutes, MaximumDurationMinutes + 1))
                : (DateTimeOffset?)null;

            plans.Add(new WorldHistoryCountPlan(
                Ordinal: ordinal,
                CountNo: countNo,
                SkuCode: dimension.SkuCode,
                UomCode: dimension.UomCode,
                SiteCode: dimension.SiteCode,
                LocationCode: dimension.LocationCode,
                LotNo: dimension.LotNo,
                ExpectedQuantity: expectedQuantity,
                VarianceQuantity: variance,
                Outcome: outcome,
                ExecutorUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.Storekeepers, countNo).UserId,
                CountDate: day,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc));
        }

        return plans;
    }

    /// <summary>已实盘回单的结局（作废与在盘的都没有实盘量）。</summary>
    public static bool IsCompletedOutcome(WorldHistoryCountOutcome outcome) =>
        outcome is WorldHistoryCountOutcome.Matched
            or WorldHistoryCountOutcome.PendingApproval
            or WorldHistoryCountOutcome.RecountRequired;

    /// <summary>结局配额（每 20 条）：账实相符 13 / 待审批 3 / 需复盘 2 / 已作废 2。</summary>
    private static WorldHistoryCountOutcome ResolveOutcome(int index) => (index % 20) switch
    {
        < 13 => WorldHistoryCountOutcome.Matched,
        < 16 => WorldHistoryCountOutcome.PendingApproval,
        < 18 => WorldHistoryCountOutcome.RecountRequired,
        _ => WorldHistoryCountOutcome.Cancelled,
    };
}

/// <summary>一条可盘维度（与库存台账的唯一索引同构，历史不使用序列号与效期维度）。</summary>
public sealed record WorldHistoryCountDimension(
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string LotNo);

/// <summary>一次循环盘点的结局。</summary>
public enum WorldHistoryCountOutcome
{
    /// <summary>账实相符：仓储侧闭环，不产生库存调整。</summary>
    Matched,

    /// <summary>有差异，调整已进入审批。</summary>
    PendingApproval,

    /// <summary>有差异，审批驳回，要求复盘。</summary>
    RecountRequired,

    /// <summary>盘点计划调整，本次任务作废。</summary>
    Cancelled,

    /// <summary>最近一批尚未回单。</summary>
    Open,
}

/// <summary>一次循环盘点的确定性计划（仓储执行侧与库存账侧共享）。</summary>
public sealed record WorldHistoryCountPlan(
    int Ordinal,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string LotNo,
    decimal ExpectedQuantity,
    decimal VarianceQuantity,
    WorldHistoryCountOutcome Outcome,
    string ExecutorUserId,
    DateOnly CountDate,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    /// <summary>仓储执行侧的实盘量 = 下发时的账面快照 + 差异量。</summary>
    public decimal CountedQuantity => ExpectedQuantity + VarianceQuantity;

    /// <summary>是否已回单（仓储侧 <c>count_executions</c> 走到 Completed）。作废与在盘的都没有实盘量。</summary>
    public bool IsCompleted => WorldHistoryCountSpec.IsCompletedOutcome(Outcome);

    /// <summary>是否在库存域产生盘点任务（零差异盘点不进库存域，见类型注释裁决点二）。</summary>
    public bool HasInventoryCountTask => Outcome != WorldHistoryCountOutcome.Matched;

    /// <summary>库存侧是否留下一行盘点调整（待审批与需复盘各留一行，后者为作废态）。</summary>
    public bool HasInventoryAdjustment =>
        Outcome is WorldHistoryCountOutcome.PendingApproval or WorldHistoryCountOutcome.RecountRequired;
}
