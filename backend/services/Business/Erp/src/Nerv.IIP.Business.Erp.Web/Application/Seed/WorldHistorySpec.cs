namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的**跨服务共享形状**（设定集 §7 / §9）。
///
/// 这是 ERP 与 MES 之间唯一的约定：两侧用同一 <c>(asOfDate, scale)</c> 调用
/// <see cref="BuildOrderPlans"/>，必须逐字段得到同一张订单计划表，
/// 于是 <c>SO-2026-#####</c> 与 <c>WO-2026-#####</c> 天然配对，无需跨服务查询或外键。
/// 两侧按同一字面量重复声明本类型，各有黄金向量测试防止漂移
/// （与 L0 <c>WorldBibleSpec</c>、千单规模块 <c>LeaderDemoScaleSpec</c> 的策略一致）。
///
/// 号段严格遵循设定集 §9，且与 <c>*-DEMO-*</c>（MAN-519 固定演示事实）、
/// <c>*-SCALE-*</c>（千单排产演示在制池）完全隔离。
/// </summary>
public static class WorldHistorySpec
{
    public const string SiteCode = "SITE-001";
    public const string CurrencyCode = "CNY";
    public const string UomCode = "pcs";

    #region §9 号段

    public static string SalesOrderNo(int index) => $"SO-2026-{index:D5}";
    public static string QuotationNo(int index) => $"QUO-2026-{index:D5}";
    public static string WorkOrderNo(int index) => $"WO-2026-{index:D5}";
    public static string DeliveryOrderNo(int index) => $"DO-2026-{index:D5}";
    public static string ReceivableNo(int index) => $"AR-2026-{index:D5}";
    public static string CashReceiptNo(int index) => $"CR-2026-{index:D5}";
    public static string RevenueVoucherNo(int index) => $"JV-2026-S{index:D5}";
    public static string CollectionVoucherNo(int index) => $"JV-2026-C{index:D5}";
    public static string PurchaseOrderNo(int index) => $"PO-2026-{index:D4}";
    public static string PurchaseReceiptNo(int index) => $"PR-2026-{index:D4}";

    /// <summary>本引擎产出的全部单据号前缀，供隔离性回归测试断言不与固定演示事实/规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes =
    [
        "SO-2026-", "QUO-2026-", "WO-2026-", "DO-2026-", "AR-2026-",
        "CR-2026-", "JV-2026-", "PO-2026-", "PR-2026-",
        "PRQ-2026-", "RFQ-2026-", "SQ-2026-", "OPP-2026-", "COST-2026-",
    ];

    #endregion

    #region §4 成品与价格

    /// <summary>热销平台（L0 <c>WorldBibleSpec.HotPlatformCodes</c>）走量更大。</summary>
    public static readonly string[] HotPlatformCodes = ["P1", "S1"];

    private static readonly string[] PlatformCodes = ["P1", "P2", "S1", "S2", "M1", "E1"];

    /// <summary>24 个成品，与 L0 <c>WorldBibleSpec.FinishedGoods</c> 同序同码。</summary>
    public static readonly IReadOnlyList<string> FinishedGoodSkus = BuildFinishedGoodSkus();

    /// <summary>成品抽样权重：热销平台 P1/S1 的 8 款权重 3，其余 1。</summary>
    public static readonly IReadOnlyList<int> FinishedGoodWeights =
        [.. FinishedGoodSkus.Select(sku => HotPlatformCodes.Any(platform => sku.Contains($"-{platform}-", StringComparison.Ordinal)) ? 3 : 1)];

    private static IReadOnlyList<string> BuildFinishedGoodSkus()
    {
        var skus = new List<string>(24);
        foreach (var platform in PlatformCodes)
        {
            foreach (var type in new[] { "QJ", "HJ" })
            {
                foreach (var side in new[] { "L", "R" })
                {
                    skus.Add($"FG-{type}-{platform}-{side}");
                }
            }
        }

        return skus;
    }

    /// <summary>
    /// 成品单价（元）：前滑柱总成比后减振器总成贵，平台序号越靠后价格略高。
    /// 纯函数、无随机，保证同一 SKU 在所有单据（订单/发货/应收/凭证）上价格一致。
    /// </summary>
    public static decimal UnitPrice(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        var platformIndex = 0;
        for (var index = 0; index < PlatformCodes.Length; index++)
        {
            if (skuCode.Contains($"-{PlatformCodes[index]}-", StringComparison.Ordinal))
            {
                platformIndex = index;
                break;
            }
        }

        var isFrontStrut = skuCode.StartsWith("FG-QJ-", StringComparison.Ordinal);
        return isFrontStrut ? 320m + (platformIndex * 15m) : 240m + (platformIndex * 12m);
    }

    #endregion

    #region §6 客户分布

    /// <summary>
    /// 设定集 §6 的 8 家客户。<c>CUST-DEMO-001</c> 是 MAN-519 固定演示事实（由 L2 拥有），
    /// 本引擎只**引用**其编码下单，不创建也不修改该客户主数据。
    /// </summary>
    public static readonly IReadOnlyList<string> CustomerCodes =
    [
        "CUST-DEMO-001",
        "CUST-WB-001",
        "CUST-WB-002",
        "CUST-WB-003",
        "CUST-WB-004",
        "CUST-WB-005",
        "CUST-WB-006",
        "CUST-WB-007",
    ];

    /// <summary>大客户占比高（设定集 §7「客户按 8 家分布，大客户占比高」）。</summary>
    public static readonly IReadOnlyList<int> CustomerWeights = [8, 10, 8, 5, 6, 3, 4, 2];

    #endregion

    #region §7 状态分布

    /// <summary>
    /// 设定集 §7 的状态分布：已收款结案 78% / 已发货待收款 8% / 在制 9% / 已下达待开工 3% / 废弃 2%。
    ///
    /// 关键设计：**按时间轴排布，而不是随机撒点**。越老的订单越靠近结案，最近的订单还在下达/在制，
    /// 这样「7 月的单还在车间、1 月的单早已收款」在页面上自洽。废弃单按 2% 独立概率全程均匀撒落。
    /// </summary>
    public const double CancelledProbability = 0.02;
    public const double SettledPositionThreshold = 0.796;
    public const double ShippedPositionThreshold = 0.878;
    public const double InProgressPositionThreshold = 0.969;

    /// <summary>
    /// 「已完工待发货」的订单条数（#1374）。
    ///
    /// 取自**已发货档的最末几张**——按时间轴排布下，那正是最近才完工的一批：
    /// 工单已完工入库、成品在成品库、发货单已开，但仓库还没拣货复核发运。
    /// 修复前全世界 2933 张出库单一律 <c>Completed</c>，演示走到「销售发货 → 拣货 → 复核 → 发运」
    /// 这条最核心的仓储链路时**一个可点的对象都没有**。
    ///
    /// 数量刻意保持个位数：它只是给演示留几个可操作对象，不是要改写 §7 的状态分布。
    /// </summary>
    public const int PendingShipmentOrderCount = 3;

    #endregion

    /// <summary>本次生成覆盖的总订单数（跨 ERP/MES 必须一致）。</summary>
    public static int TotalOrders(DateOnly asOfDate, double scale)
    {
        var total = 0;
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        for (var week = 0; week < weeks; week++)
        {
            total += WorldHistoryCalendar.WeeklyOrderVolume(week, scale);
        }

        return total;
    }

    /// <summary>
    /// 生成全量订单计划表。ERP 按它写销售侧全链，MES 按它写工单侧全链；
    /// 两侧不通信、不查库，仅靠本函数的确定性达成一致。
    /// </summary>
    public static IReadOnlyList<WorldHistoryOrderPlan> BuildOrderPlans(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        var total = TotalOrders(asOfDate, scale);
        var plans = new List<WorldHistoryOrderPlan>(total);

        var index = 0;
        for (var week = 0; week < weeks; week++)
        {
            var volume = WorldHistoryCalendar.WeeklyOrderVolume(week, scale);
            var weekStart = WorldHistoryCalendar.WeekStart(week);
            for (var slot = 0; slot < volume; slot++)
            {
                index++;
                var orderDate = ResolveOrderDate(weekStart, slot, volume, asOfDate);
                plans.Add(BuildOrderPlan(index, total, orderDate));
            }
        }

        return PromotePendingShipments(plans);
    }

    /// <summary>
    /// 把已发货档最末几张改判「已完工待发货」（#1374，条数见 <see cref="PendingShipmentOrderCount"/>）。
    ///
    /// <para>
    /// 裁决点一 · **改判必须是后置遍历，不能塞进 <c>ResolveStage</c>**。
    /// <c>BuildOrderPlan</c> 里 <c>ResolveStage</c> 之后还要抽 <c>leadTimeDays</c>；
    /// 在阶段判定里多消费或少消费一次抽样，会把全世界每张订单的交期整体挪位。
    /// 本方法只重写已建好计划的 <c>Stage</c> 字段，**零抽样消费**，其余字段逐字不变。
    /// </para>
    /// <para>
    /// 裁决点二 · **取已发货档而不是最新订单**。待发货的前提是工单已完工入库；
    /// 在制 / 已下达档的成品根本还没进库，挂发货单是假事实。已发货档的末尾在时间轴上
    /// 紧邻在制档，正是「最近完工、交期就在眼前」的那几张。
    /// </para>
    /// </summary>
    private static IReadOnlyList<WorldHistoryOrderPlan> PromotePendingShipments(
        List<WorldHistoryOrderPlan> plans)
    {
        var promoted = 0;
        for (var cursor = plans.Count - 1; cursor >= 0 && promoted < PendingShipmentOrderCount; cursor--)
        {
            if (plans[cursor].Stage != WorldHistoryOrderStage.Shipped)
            {
                continue;
            }

            plans[cursor] = plans[cursor] with { Stage = WorldHistoryOrderStage.PendingShipment };
            promoted++;
        }

        return plans;
    }

    /// <summary>把本周的订单摊到周一–周六（周日停产），并保证不越过 <paramref name="asOfDate"/>。</summary>
    private static DateOnly ResolveOrderDate(DateOnly weekStart, int slot, int volume, DateOnly asOfDate)
    {
        var workingDays = new List<DateOnly>(6);
        for (var offset = 0; offset < 7; offset++)
        {
            var candidate = weekStart.AddDays(offset);
            if (WorldHistoryCalendar.IsWorkingDay(candidate) && candidate <= asOfDate)
            {
                workingDays.Add(candidate);
            }
        }

        if (workingDays.Count == 0)
        {
            return asOfDate;
        }

        // 均匀摊到本周可用工作日，slot 顺序即当周下单顺序，保证单号与日期同向单调。
        var dayIndex = volume <= 1 ? 0 : slot * workingDays.Count / volume;
        return workingDays[Math.Min(dayIndex, workingDays.Count - 1)];
    }

    private static WorldHistoryOrderPlan BuildOrderPlan(int index, int total, DateOnly orderDate)
    {
        var salesOrderNo = SalesOrderNo(index);
        var random = new WorldHistoryRandom(salesOrderNo);
        var skuCode = random.PickWeighted(FinishedGoodSkus, FinishedGoodWeights);
        var customerCode = random.PickWeighted(CustomerCodes, CustomerWeights);
        var quantity = ResolveQuantity(random);
        var stage = ResolveStage(random, index, total);
        var leadTimeDays = random.NextInt(18, 41);

        return new WorldHistoryOrderPlan(
            Index: index,
            SalesOrderNo: salesOrderNo,
            QuotationNo: QuotationNo(index),
            WorkOrderNo: WorkOrderNo(index),
            SkuCode: skuCode,
            CustomerCode: customerCode,
            Quantity: quantity,
            UnitPrice: UnitPrice(skuCode),
            OrderDate: orderDate,
            RequiredDate: orderDate.AddDays(leadTimeDays),
            Stage: stage);
    }

    /// <summary>订单数量分档：小批多、大批少，步长 20 让数字看起来像人下的。</summary>
    private static decimal ResolveQuantity(WorldHistoryRandom random)
    {
        var tier = random.PickWeighted([0, 1, 2], [5, 3, 1]);
        return tier switch
        {
            0 => random.NextQuantity(40, 120, 20),
            1 => random.NextQuantity(140, 320, 20),
            _ => random.NextQuantity(340, 600, 20),
        };
    }

    private static WorldHistoryOrderStage ResolveStage(WorldHistoryRandom random, int index, int total)
    {
        if (random.Chance(CancelledProbability))
        {
            return WorldHistoryOrderStage.Cancelled;
        }

        var position = total <= 1 ? 0d : (double)(index - 1) / total;
        return position switch
        {
            < SettledPositionThreshold => WorldHistoryOrderStage.Settled,
            < ShippedPositionThreshold => WorldHistoryOrderStage.Shipped,
            < InProgressPositionThreshold => WorldHistoryOrderStage.InProgress,
            _ => WorldHistoryOrderStage.Released,
        };
    }
}

/// <summary>
/// 单张历史订单的生命周期阶段（设定集 §7 状态分布）。
///
/// 平台的 <c>SalesOrder.Status</c> 只有 released / credit-held / cancelled 三态，
/// 「已收款结案 / 已发货待收款 / 在制 / 已下达」是**订单 + 发货单 + 应收 + 工单**的组合态，
/// 由本枚举统一驱动两侧生成（裁决点见 PR 正文）。
/// </summary>
public enum WorldHistoryOrderStage
{
    /// <summary>已收款结案：全量发货 + 应收全额收款 + 收入与收款两张凭证。</summary>
    Settled,

    /// <summary>已发货待收款：全量发货 + 应收挂账未收款 + 仅收入凭证。</summary>
    Shipped,

    /// <summary>
    /// 已完工待发货（#1374）：工单已完工入库、成品在成品库、发货单已开但**未发运**，
    /// 因而没有发货出库流水、没有应收、没有凭证。仓储侧留一张可拣可复核可发运的出库单。
    /// </summary>
    PendingShipment,

    /// <summary>在制：工单已开工并有部分报工，未发货、无应收。</summary>
    InProgress,

    /// <summary>已下达待开工：工单已下达、工序任务排队中，无报工。</summary>
    Released,

    /// <summary>废弃：订单已取消，不产生工单/发货/应收。</summary>
    Cancelled,
}

/// <summary>单张历史订单的跨服务计划（ERP 与 MES 必须逐字段一致）。</summary>
public sealed record WorldHistoryOrderPlan(
    int Index,
    string SalesOrderNo,
    string QuotationNo,
    string WorkOrderNo,
    string SkuCode,
    string CustomerCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly OrderDate,
    DateOnly RequiredDate,
    WorldHistoryOrderStage Stage)
{
    /// <summary>订单金额（单行订单，行号固定 "10"）。</summary>
    public decimal TotalAmount => Quantity * UnitPrice;

    /// <summary>该阶段是否应产生工单（MES 侧据此决定是否写 <c>WO-2026-#####</c>）。</summary>
    public bool HasWorkOrder => Stage != WorldHistoryOrderStage.Cancelled;

    /// <summary>该阶段是否**已发运**（发货出库流水、应收与收入凭证都由它驱动）。</summary>
    public bool HasDelivery => Stage is WorldHistoryOrderStage.Settled or WorldHistoryOrderStage.Shipped;

    /// <summary>
    /// 该阶段是否有一张**已开未发运**的发货单（#1374）——演示当天由仓库拣货、复核、发运。
    /// </summary>
    public bool HasPendingShipment => Stage == WorldHistoryOrderStage.PendingShipment;

    /// <summary>
    /// 该阶段的工单是否已完工入库（成品已在成品库）。
    ///
    /// 与 <see cref="HasDelivery"/> 的区别只在**发没发运**：待发货档的成品同样在库，
    /// 因此出货检验、装箱标签、补产工单挑选这些「完工即成立」的事实一律用本谓词，
    /// 而不是用发运与否去判定——否则 #1374 抽走三张单会连带挪动补产工单的挑选下标。
    /// </summary>
    public bool IsProductionClosed =>
        Stage is WorldHistoryOrderStage.Settled
            or WorldHistoryOrderStage.Shipped
            or WorldHistoryOrderStage.PendingShipment;

    /// <summary>该阶段是否应收款结案。</summary>
    public bool IsCollected => Stage == WorldHistoryOrderStage.Settled;
}
