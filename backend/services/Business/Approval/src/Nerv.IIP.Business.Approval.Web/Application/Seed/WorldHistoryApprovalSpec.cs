namespace Nerv.IIP.Business.Approval.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **审批域侧**的确定性事实流。
///
/// 两条审批来源全部**引用已有的共享形状**，审批域不自造任何业务单据：
/// <list type="number">
/// <item>采购订单审批（<c>purchase-order</c> / <c>erp</c>）：逐张覆盖一期 ERP 的 <c>PO-2026-####</c>，
/// 发起人是经营部 2 名采购（设定集 §5 EMP-057/EMP-058），审批人是厂长（<c>user-admin</c>）；</item>
/// <item>NCR 处置审批（<c>ncr-disposition</c> / <c>quality</c>）：引用二期质量域的 <c>NCR-2026-####</c> 号段，
/// 发起人是质量工程师（EMP-040/EMP-041），审批人是质量主管（EMP-033 胡玉兰）。</item>
/// </list>
/// 因此审批链上的每个源单据号在 ERP / Quality 库里都真实存在，无需跨服务查询或外键（与一期同一手法）。
///
/// 本类型是纯函数：同一 <c>(asOfDate, scale)</c> 必得同一张事实表，seed 与校验器共用它，
/// 于是「写入的东西」与「校验的东西」不可能漂移。
/// </summary>
public static class WorldHistoryApprovalSpec
{
    #region 模板（世界观历史专用，与 *-DEMO-* 固定演示事实隔离）

    public const string PurchaseTemplateCode = "APT-WB-PO-001";
    public const string PurchaseDocumentType = "purchase-order";
    public const string PurchaseSourceService = "erp";

    public const string NcrTemplateCode = "APT-WB-NCR-001";
    public const string NcrDocumentType = "ncr-disposition";
    public const string NcrSourceService = "quality";

    /// <summary>本引擎产出/引用的全部号段前缀，供隔离性回归测试断言不与固定演示事实相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["PO-2026-", "NCR-2026-", "APT-WB-"];

    #endregion

    #region 世界观人物（设定集 §5，user id 公式与 L0 <c>WorldBibleSpec.BuildEmployees</c> 对齐）

    public const string ActorTypeUser = "user";

    /// <summary>厂长 / 管理员：IAM seed 的 <c>AdminUserId</c>，工作台待办按该 principal id 过滤。</summary>
    public const string AdminUserId = "user-admin";

    /// <summary>质量主管（EMP-033 胡玉兰），NCR 处置审批人。</summary>
    public const string QualitySupervisorUserId = "user-emp-033";

    /// <summary>经营部 2 名采购（EMP-057 何志强 / EMP-058 郭晓东），采购订单审批发起人。</summary>
    public static readonly IReadOnlyList<WorldHistoryPerson> Purchasers =
    [
        new("user-emp-057", "EMP-057"),
        new("user-emp-058", "EMP-058"),
    ];

    /// <summary>质量部 2 名质量工程师（EMP-040/EMP-041），NCR 处置审批发起人（与质量域 MRB 评审同一人群）。</summary>
    public static readonly IReadOnlyList<WorldHistoryPerson> QualityEngineers =
    [
        new("user-emp-040", "EMP-040"),
        new("user-emp-041", "EMP-041"),
    ];

    /// <summary>按流键在人员池里确定性取人（与二期 <c>WorldHistoryPhase2Spec.Assign</c> 同字面量）。</summary>
    public static WorldHistoryPerson Assign(IReadOnlyList<WorldHistoryPerson> pool, string streamKey) =>
        new WorldHistoryRandom($"assign:{streamKey}").Pick(pool);

    #endregion

    #region 结果分布

    /// <summary>已完成审批中约 5% 被驳回（价格 / 预算类原因），其余通过。</summary>
    public const double RejectedProbability = 0.05;

    /// <summary>挂在厂长（admin）名下的待办审批目标条数（5–8 条口径取 6）。</summary>
    public const int PendingTargetCount = 6;

    private static readonly IReadOnlyList<string?> ApproveComments =
    [
        "同意，按计划执行采购",
        "同意，价格在框架协议范围内",
        "同意，注意跟进交期",
        null,
    ];

    private static readonly IReadOnlyList<string> RejectComments =
    [
        "单价高于上月成交价，退回重新议价",
        "本月该物料预算已用尽，驳回",
        "供应商近期交付逾期率偏高，驳回换供应商比价",
    ];

    #endregion

    /// <summary>NCR 号段公式，与质量域 <c>WorldHistoryPhase2Spec.NonconformanceReportNo</c> 同字面量。</summary>
    public static string NonconformanceReportNo(int index) => $"NCR-2026-{index:D4}";

    /// <summary>
    /// 引用的 NCR 条数（保守下界）——**这是一个经标定的建模选择（裁决点）**。
    ///
    /// 质量域的 NCR 总数由完整的一期工单 / 收货 / 发货事实流按 2.3% 不合格率涌现，
    /// 在审批域逐字面量复算它需要复制整套 ERP+MES+Quality 规格，远超共享形状的合理边界。
    /// 由于 <c>NCR-2026-####</c> 从 1 起连续编号，只要引用条数不超过质量域的实际条数，
    /// 引用 <c>1..K</c> 就全部真实存在。故取
    /// <c>K = floor(0.18 × TotalPurchaseOrders)</c>（采购单量是同一节奏曲线的代理量），
    /// 并在小样本区间（<c>scale &lt; 0.05</c> 或采购单不足 30 张）直接取 0。
    ///
    /// 标定依据：对 2026 全年每个周一 × scale {1.0, 0.5, 0.35, 0.2, 0.1, 0.05}、
    /// 以及 2026-07-01..08-15 每日 × scale 1.0 逐点复算质量域实际 NCR 数，
    /// 本下界处处 ≤ 实际值（实测比值最低 0.265，本系数 0.18 留有 ≥30% 余量）。
    /// 语义上即「重大处置需审批的 NCR 子集」，无需覆盖全部 NCR。
    /// </summary>
    public static int NcrReferenceCount(DateOnly asOfDate, double scale)
    {
        if (scale < 0.05d)
        {
            return 0;
        }

        var totalPurchaseOrders = WorldHistoryProcurementSpec.TotalPurchaseOrders(asOfDate, scale);
        return totalPurchaseOrders < 30 ? 0 : (int)Math.Floor(totalPurchaseOrders * 0.18d);
    }

    /// <summary>
    /// 全量审批事实流。顺序固定为「采购订单审批（按采购单序号）→ NCR 处置审批（按 NCR 序号）」，
    /// 因此同一 <c>(asOfDate, scale)</c> 下事实表稳定。
    /// </summary>
    public static IReadOnlyList<WorldHistoryApprovalFact> BuildApprovalFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var purchasePlans = WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale);
        var facts = new List<WorldHistoryApprovalFact>(purchasePlans.Count + 64);

        // 待办 = 序号最大（也就是时间上最新）的几张采购单：厂长的待办卡里挂的是「上周刚提交的采购申请」。
        var pendingCount = Math.Min(PendingTargetCount, purchasePlans.Count);
        var firstPendingIndex = purchasePlans.Count - pendingCount;

        foreach (var plan in purchasePlans)
        {
            facts.Add(BuildPurchaseFact(plan, plan.Index > firstPendingIndex, asOfDate));
        }

        var ncrCount = NcrReferenceCount(asOfDate, scale);
        var historyDaySpan = asOfDate.DayNumber - WorldHistoryCalendar.GoLiveDate.DayNumber + 1;
        for (var sequence = 1; sequence <= ncrCount; sequence++)
        {
            facts.Add(BuildNcrFact(sequence, historyDaySpan, asOfDate));
        }

        return facts;
    }

    private static WorldHistoryApprovalFact BuildPurchaseFact(WorldHistoryPurchasePlan plan, bool pending, DateOnly asOfDate)
    {
        var purchaseOrderNo = plan.PurchaseOrderNo;
        var startedAtUtc = MomentOn(ClampToHistory(plan.OrderDate, asOfDate), purchaseOrderNo, "approval-start");
        var startedBy = Assign(Purchasers, purchaseOrderNo);

        if (pending)
        {
            return new WorldHistoryApprovalFact(
                TemplateCode: PurchaseTemplateCode,
                SourceService: PurchaseSourceService,
                DocumentType: PurchaseDocumentType,
                DocumentId: purchaseOrderNo,
                Amount: plan.TotalAmount,
                StartedByUserId: startedBy.UserId,
                ApproverUserId: AdminUserId,
                Outcome: WorldHistoryApprovalOutcome.Pending,
                DecisionComment: null,
                StartedAtUtc: startedAtUtc,
                DecidedAtUtc: null);
        }

        var random = new WorldHistoryRandom($"approval-outcome:{purchaseOrderNo}");
        var rejected = random.Chance(RejectedProbability);
        var decidedAtUtc = Later(
            MomentOn(ClampToHistory(WorldHistoryCalendar.AddWorkingDays(plan.OrderDate, 1), asOfDate), purchaseOrderNo, "approval-decision"),
            startedAtUtc.AddMinutes(45));

        return new WorldHistoryApprovalFact(
            TemplateCode: PurchaseTemplateCode,
            SourceService: PurchaseSourceService,
            DocumentType: PurchaseDocumentType,
            DocumentId: purchaseOrderNo,
            Amount: plan.TotalAmount,
            StartedByUserId: startedBy.UserId,
            ApproverUserId: AdminUserId,
            Outcome: rejected ? WorldHistoryApprovalOutcome.Rejected : WorldHistoryApprovalOutcome.Approved,
            DecisionComment: rejected
                ? random.Pick(RejectComments)
                : random.Pick(ApproveComments),
            StartedAtUtc: startedAtUtc,
            DecidedAtUtc: decidedAtUtc);
    }

    private static WorldHistoryApprovalFact BuildNcrFact(int sequence, int historyDaySpan, DateOnly asOfDate)
    {
        var ncrCode = NonconformanceReportNo(sequence);
        // NCR 的确切开单日只在质量库里；审批侧按 NCR 号取流，把处置审批散布在历史工作日内。
        var dayOffset = new WorldHistoryRandom($"approval-ncr-day:{ncrCode}").NextInt(0, historyDaySpan);
        var day = ClampToHistory(WorldHistoryCalendar.GoLiveDate.AddDays(dayOffset), asOfDate);
        var startedAtUtc = MomentOn(day, ncrCode, "approval-start");
        var decidedAtUtc = Later(
            MomentOn(ClampToHistory(WorldHistoryCalendar.AddWorkingDays(day, 1), asOfDate), ncrCode, "approval-decision"),
            startedAtUtc.AddMinutes(45));

        return new WorldHistoryApprovalFact(
            TemplateCode: NcrTemplateCode,
            SourceService: NcrSourceService,
            DocumentType: NcrDocumentType,
            DocumentId: ncrCode,
            Amount: null,
            StartedByUserId: Assign(QualityEngineers, ncrCode).UserId,
            ApproverUserId: QualitySupervisorUserId,
            // NCR 处置审批全部通过：质量域的历史 NCR 都以「MRB 评审通过 → 处置 → 关单」收尾，
            // 审批侧若出现驳回会与源域的关单事实自相矛盾。
            Outcome: WorldHistoryApprovalOutcome.Approved,
            DecisionComment: "MRB 处置评审通过，同意处置方案",
            StartedAtUtc: startedAtUtc,
            DecidedAtUtc: decidedAtUtc);
    }

    /// <summary>把「工作日 + 流键」映射到一个确定性的班内 UTC 时刻（与二期 <c>WorldHistoryPhase2Spec.MomentOn</c> 同字面量）。</summary>
    public static DateTimeOffset MomentOn(DateOnly date, string streamKey, string purpose)
    {
        var workingDay = WorldHistoryCalendar.SnapToWorkingDay(date);
        var random = new WorldHistoryRandom($"{purpose}:{streamKey}");
        var shiftIndex = random.NextInt(0, 2);
        var minutesIntoShift = random.NextInt(0, WorldHistoryCalendar.ShiftLengthHours * 60);
        return WorldHistoryCalendar.ShiftMoment(workingDay, shiftIndex, minutesIntoShift);
    }

    /// <summary>把候选日期夹进 <c>[上线日, asOfDate]</c> 并回退到工作日（周日停产，历史里不得出现周日事件）。</summary>
    public static DateOnly ClampToHistory(DateOnly candidate, DateOnly asOfDate)
    {
        var cursor = candidate > asOfDate ? asOfDate : candidate;
        if (cursor < WorldHistoryCalendar.GoLiveDate)
        {
            cursor = WorldHistoryCalendar.GoLiveDate;
        }

        while (!WorldHistoryCalendar.IsWorkingDay(cursor) && cursor > WorldHistoryCalendar.GoLiveDate)
        {
            cursor = cursor.AddDays(-1);
        }

        return cursor;
    }

    private static DateTimeOffset Later(DateTimeOffset candidate, DateTimeOffset floor) =>
        candidate > floor ? candidate : floor;
}

/// <summary>世界观人物（user id + 员工号），与 L0 <c>WorldBibleSpec</c> 的 <c>user-emp-0xx</c> 公式对齐。</summary>
public sealed record WorldHistoryPerson(string UserId, string EmployeeNo);

/// <summary>审批链在历史里的落点结果。</summary>
public enum WorldHistoryApprovalOutcome
{
    /// <summary>待办：挂在审批人名下，工作台待办卡据此有数。</summary>
    Pending,

    /// <summary>已通过。</summary>
    Approved,

    /// <summary>已驳回（终态，约 5%）。</summary>
    Rejected,
}

/// <summary>一条历史审批事实（模板 + 源单据引用 + 发起/审批人 + 结果 + 回填时间）。</summary>
public sealed record WorldHistoryApprovalFact(
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    decimal? Amount,
    string StartedByUserId,
    string ApproverUserId,
    WorldHistoryApprovalOutcome Outcome,
    string? DecisionComment,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? DecidedAtUtc)
{
    /// <summary>发起人的公开 actor 引用（<c>user:user-emp-057</c>），与 Gateway 注入口径一致。</summary>
    public string StartedByActorRef => $"{WorldHistoryApprovalSpec.ActorTypeUser}:{StartedByUserId}";

    /// <summary>是否已走到终态（通过 / 驳回）。</summary>
    public bool IsCompleted => Outcome != WorldHistoryApprovalOutcome.Pending;
}
