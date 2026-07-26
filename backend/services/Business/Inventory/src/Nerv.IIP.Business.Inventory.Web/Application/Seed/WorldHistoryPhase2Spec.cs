namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// L1 背景历史 **二期**（质量 / 库存 / 仓储 / 条码标签）的跨服务共享形状。
///
/// 一期（#1128）用 <see cref="WorldHistorySpec.BuildOrderPlans"/> 让 ERP 与 MES 在不通信的前提下
/// 对齐同一张订单计划表。二期沿用同一手法再往下推一层：四个服务用同一
/// <c>(asOfDate, scale)</c> 调用 <see cref="BuildWorkOrderFacts"/> 与
/// <see cref="WorldHistoryProcurementSpec.BuildPurchasePlans"/>，得到逐字段相同的
/// 「工单 / 收货」事实流，于是
///
/// <list type="bullet">
/// <item>质量的检验任务挂在真实存在的工单终检工序与采购收货上；</item>
/// <item>库存的移动流水与 MES 的领料、完工入库、ERP 的发货逐笔对应；</item>
/// <item>仓储的收货/上架/拣货/出库单据与库存流水同号同时刻；</item>
/// <item>条码扫码记录的时间戳与源单据一致。</item>
/// </list>
///
/// 四侧按同一字面量重复声明本类型，各有黄金向量测试防止漂移
/// （与一期 <see cref="WorldHistorySpec"/>、L0 <c>WorldBibleSpec</c> 的策略一致）。
/// </summary>
public static class WorldHistoryPhase2Spec
{
    #region §9 号段（二期新增，与 *-DEMO-* / *-SCALE-* 完全隔离）

    /// <summary>不合格报告（设定集 §9 已预留 <c>NCR-2026-####</c>）。</summary>
    public static string NonconformanceReportNo(int index) => $"NCR-2026-{index:D4}";

    /// <summary>入库单（采购收货 / 完工入库各自成单）。</summary>
    public static string InboundOrderNo(string sourceDocumentId) => $"IB-{sourceDocumentId}";

    /// <summary>出库单（销售发货 / 车间领料各自成单）。</summary>
    public static string OutboundOrderNo(string sourceDocumentId) => $"OB-{sourceDocumentId}";

    /// <summary>仓储作业任务（上架 / 拣货）。</summary>
    public static string WarehouseTaskNo(string orderNo, int ordinal) => $"WT-{orderNo}-{ordinal:D2}";

    /// <summary>库存移动幂等键：同一源单据行只落一笔流水。</summary>
    public static string MovementKey(string sourceDocumentId, string purpose) => $"{sourceDocumentId}:{purpose}";

    /// <summary>标签打印批次幂等键。</summary>
    public static string PrintBatchKey(string sourceDocumentId, string templateCode) => $"PB-{sourceDocumentId}-{templateCode}";

    /// <summary>扫码记录幂等键。</summary>
    public static string ScanKey(string sourceDocumentId, int ordinal) => $"SCAN-{sourceDocumentId}-{ordinal:D2}";

    /// <summary>二期产出的全部单据号前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes =
    [
        "NCR-2026-", "IB-", "OB-", "WT-", "LOT-", "PB-", "SCAN-", "TPL-WB-", "BR-WB-",
    ];

    #endregion

    #region §2 库位（世界观历史专用，与 DEMO-* 库位隔离）

    /// <summary>收货暂存区：采购收货先落这里，待检合格后上架。</summary>
    public const string ReceivingStagingLocationCode = "WH-WB-STG-01";

    /// <summary>原料库（含外购弹簧、棒料、钢管、密封件、减振油、包材）。</summary>
    public const string RawMaterialLocationCode = "WH-WB-RM-01";

    /// <summary>半成品库（活塞杆 / 缸筒 / 阀系组件）。</summary>
    public const string SemiFinishedLocationCode = "WH-WB-SF-01";

    /// <summary>成品库。</summary>
    public const string FinishedGoodsLocationCode = "WH-WB-FG-01";

    /// <summary>不合格品隔离区（NCR 报废 / 让步待判定期间的持有痕迹落在这里）。</summary>
    public const string QualityHoldLocationCode = "WH-WB-QC-01";

    /// <summary>车间线边库：领料后物料的去向。</summary>
    public const string LineSideLocationCode = "WH-WB-LINE-01";

    public static readonly IReadOnlyList<WorldHistoryStockLocation> StockLocations =
    [
        new(ReceivingStagingLocationCode, "收货暂存区", "staging"),
        new(RawMaterialLocationCode, "原料库", "storage"),
        new(SemiFinishedLocationCode, "半成品库", "storage"),
        new(FinishedGoodsLocationCode, "成品库", "storage"),
        new(QualityHoldLocationCode, "不合格品隔离区", "quality-hold"),
        new(LineSideLocationCode, "车间线边库", "line-side"),
    ];

    /// <summary>某物料的常驻库位：半成品进半成品库，成品进成品库，其余进原料库。</summary>
    public static string StorageLocationFor(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        if (skuCode.StartsWith("FG-", StringComparison.Ordinal))
        {
            return FinishedGoodsLocationCode;
        }

        return skuCode.StartsWith("SF-", StringComparison.Ordinal)
            ? SemiFinishedLocationCode
            : RawMaterialLocationCode;
    }

    #endregion

    #region §5 二期引用的 L0 人员（EMP 序号按 WorldBibleSpec.Employees 的部门顺序推导）

    // L0 §5 的部门顺序：生产部 28（EMP-001..028）→ 计划部 4（029..032）
    // → 质量部 9（质量主管 033、检验员 034..039、质量工程师 040..041）
    // → 设备部 6（042..047）→ 仓储物流部 7（仓储主管 048、库管 049..052、叉车 053..054）
    // → 经营部 4（055..058）。

    /// <summary>质量部 6 名检验员（设定集 §5），检验任务在他们之间轮转分配。</summary>
    public static readonly IReadOnlyList<WorldHistoryPerson> Inspectors = BuildPeople(34, 6);

    /// <summary>质量部 2 名质量工程师，负责 NCR 处置评审。</summary>
    public static readonly IReadOnlyList<WorldHistoryPerson> QualityEngineers = BuildPeople(40, 2);

    /// <summary>仓储物流部 4 名库管，收货/上架/拣货/出库作业的执行人。</summary>
    public static readonly IReadOnlyList<WorldHistoryPerson> Storekeepers = BuildPeople(49, 4);

    private static IReadOnlyList<WorldHistoryPerson> BuildPeople(int firstOrdinal, int count) =>
        [.. Enumerable.Range(firstOrdinal, count)
            .Select(ordinal => new WorldHistoryPerson($"user-emp-{ordinal:D3}", $"EMP-{ordinal:D3}"))];

    /// <summary>按流键在人员池里确定性取人。</summary>
    public static WorldHistoryPerson Assign(IReadOnlyList<WorldHistoryPerson> pool, string streamKey) =>
        new WorldHistoryRandom($"assign:{streamKey}").Pick(pool);

    #endregion

    #region 工单事实流（二期四侧共享）

    /// <summary>
    /// 全量工单事实（订单工单 + 补产工单），与 MES 一期 <c>WorldHistorySeedService</c> 的写入顺序
    /// 与内容逐字段一致。二期各域据此挂检验任务、库存流水、仓储单据与扫码记录。
    /// </summary>
    public static IReadOnlyList<WorldHistoryWorkOrderFact> BuildWorkOrderFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale)
            .Where(plan => plan.HasWorkOrder)
            .ToArray();

        var facts = new List<WorldHistoryWorkOrderFact>(plans.Length + (plans.Length / 8) + 1);
        foreach (var plan in plans)
        {
            facts.Add(new WorldHistoryWorkOrderFact(
                WorldHistoryMesSpec.BuildWorkOrderPlan(plan.WorkOrderNo, plan.SkuCode, plan.Quantity),
                WorldHistoryTimeline.For(plan, asOfDate),
                ResolveExecution(plan.Stage),
                plan,
                IsRework: false));
        }

        // 补产工单：与 MES 侧同一挑选公式（只挂在已发货/已结案的订单之后）。
        var candidates = plans.Where(plan => plan.HasDelivery).ToArray();
        var reworkCount = (int)Math.Round(plans.Length * WorldHistoryMesSpec.ReworkWorkOrderRatio, MidpointRounding.AwayFromZero);
        if (candidates.Length == 0 || reworkCount == 0)
        {
            return facts;
        }

        for (var sequence = 1; sequence <= reworkCount; sequence++)
        {
            var workOrderNo = WorldHistoryMesSpec.ReworkWorkOrderNo(sequence);
            var source = candidates[(sequence - 1) * candidates.Length / reworkCount];
            var random = new WorldHistoryRandom($"rework:{workOrderNo}");
            var quantity = Math.Max(2m, decimal.Round(source.Quantity * (random.NextInt(3, 9) / 100m), 0, MidpointRounding.AwayFromZero));
            facts.Add(new WorldHistoryWorkOrderFact(
                WorldHistoryMesSpec.BuildWorkOrderPlan(workOrderNo, source.SkuCode, quantity),
                WorldHistoryTimeline.For(source, asOfDate),
                WorldHistoryExecutionDepth.Closed,
                source,
                IsRework: true));
        }

        return facts;
    }

    /// <summary>销售订单阶段 → 工单执行深度（与 MES 一期 <c>ResolveExecution</c> 同字面量）。</summary>
    public static WorldHistoryExecutionDepth ResolveExecution(WorldHistoryOrderStage stage) => stage switch
    {
        WorldHistoryOrderStage.Settled or WorldHistoryOrderStage.Shipped => WorldHistoryExecutionDepth.Closed,
        WorldHistoryOrderStage.InProgress => WorldHistoryExecutionDepth.Partial,
        _ => WorldHistoryExecutionDepth.ReleasedOnly,
    };

    #endregion

    #region 领料明细（与 MES 一期 WriteMaterialFacts 同字面量）

    /// <summary>
    /// 一张工单的领料明细。与 MES 侧 <c>WriteMaterialFacts</c> 逐条对应：
    /// 4 项主料 ×（分批工单 2 批 / 其余 1 批），序号从 1 连续递增，
    /// 于是领料单号 <c>MIR-{工单号}-{序号}</c> 在 MES 与二期各域指向同一笔事实。
    /// </summary>
    public static IReadOnlyList<WorldHistoryMaterialIssue> MaterialIssues(WorldHistoryWorkOrderFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Execution == WorldHistoryExecutionDepth.ReleasedOnly)
        {
            return [];
        }

        var plan = fact.Plan;
        var issues = new List<WorldHistoryMaterialIssue>(8);
        var ordinal = 0;
        foreach (var component in WorldHistoryMesSpec.Components(plan.SkuCode))
        {
            var required = component.QuantityPer * plan.WorkOrderQuantity;
            var portions = plan.SplitMaterialIssue
                ? new[] { decimal.Round(required * 0.6m, 2), required - decimal.Round(required * 0.6m, 2) }
                : [required];

            for (var portionIndex = 0; portionIndex < portions.Length; portionIndex++)
            {
                ordinal++;
                issues.Add(new WorldHistoryMaterialIssue(
                    RequestNo: WorldHistoryMesSpec.MaterialIssueRequestNo(plan.WorkOrderNo, ordinal),
                    SkuCode: component.SkuCode,
                    UomCode: component.UomCode,
                    Quantity: portions[portionIndex],
                    IssueDate: portionIndex == 0 ? fact.Timeline.ProductionStartDate : fact.Timeline.ProductionCompletionDate,
                    LotNo: $"LOT-{component.SkuCode}-{plan.WorkOrderNo}"));
            }
        }

        return issues;
    }

    #endregion

    /// <summary>把「工作日 + 流键」映射到一个确定性的班内 UTC 时刻（与一期 <c>MomentOn</c> 同字面量）。</summary>
    public static DateTimeOffset MomentOn(DateOnly date, string streamKey, string purpose)
    {
        var workingDay = WorldHistoryCalendar.SnapToWorkingDay(date);
        var random = new WorldHistoryRandom($"{purpose}:{streamKey}");
        var shiftIndex = random.NextInt(0, 2);
        var minutesIntoShift = random.NextInt(0, WorldHistoryCalendar.ShiftLengthHours * 60);
        return WorldHistoryCalendar.ShiftMoment(workingDay, shiftIndex, minutesIntoShift);
    }
}

/// <summary>工单的执行深度（与一期 MES <c>WorldHistoryExecution</c> 同义，二期各域各自声明）。</summary>
public enum WorldHistoryExecutionDepth
{
    /// <summary>已下达待开工：无领料、无报工、无完工入库。</summary>
    ReleasedOnly,

    /// <summary>在制：有领料与部分报工，尚无完工入库。</summary>
    Partial,

    /// <summary>已完工关单：领料、报工、完工入库齐全。</summary>
    Closed,
}

public sealed record WorldHistoryPerson(string UserId, string EmployeeNo);

public sealed record WorldHistoryStockLocation(string LocationCode, string LocationName, string LocationType);

public sealed record WorldHistoryMaterialIssue(
    string RequestNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    DateOnly IssueDate,
    string LotNo);

/// <summary>一张历史工单在二期各域眼中的事实（与 MES 一期写入的工单逐字段一致）。</summary>
public sealed record WorldHistoryWorkOrderFact(
    WorldHistoryWorkOrderPlan Plan,
    WorldHistoryTimeline Timeline,
    WorldHistoryExecutionDepth Execution,
    WorldHistoryOrderPlan SourceOrder,
    bool IsRework)
{
    /// <summary>成品批次号 <c>LOT-{工单号}</c>（一期已预留）。</summary>
    public string ProducedLotNo => WorldHistoryMesSpec.ProducedLotNo(Plan.WorkOrderNo);

    /// <summary>完工入库请求号。</summary>
    public string FinishedGoodsReceiptNo => WorldHistoryMesSpec.FinishedGoodsReceiptNo(Plan.WorkOrderNo);

    /// <summary>完工入库对应的库存移动 id <c>INV-{工单号}</c>（一期 <c>MarkPosted</c> 已写入该值）。</summary>
    public string FinishedGoodsMovementId => $"INV-{Plan.WorkOrderNo}";

    /// <summary>本工单是否走到完工入库。</summary>
    public bool HasFinishedGoodsReceipt => Execution == WorldHistoryExecutionDepth.Closed;

    /// <summary>本工单是否有性能终检工序（质量域挂检验任务的判据）。</summary>
    public bool HasFinalInspection => Plan.RequiresQualityInspection;
}
