using Nerv.IIP.Contracts.Approval;

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

    /// <summary>
    /// 采购订单下达审批模板码：取自审批契约的唯一事实来源，ERP 发起侧 / 种子 / 界面三方共用
    /// （#1344：ERP 此前硬编码 <c>erp-purchase-order-release</c>，种子态转单 / RFQ 必 400）。
    /// </summary>
    public const string PurchaseTemplateCode = ApprovalTemplateCodes.PurchaseOrderRelease;
    public const string PurchaseDocumentType = ApprovalDocumentTypes.PurchaseOrder;
    public const string PurchaseSourceService = "erp";

    public const string NcrTemplateCode = "APT-WB-NCR-001";

    /// <summary>
    /// NCR 处置审批的单据类型：取自审批契约的唯一事实来源，种子 / 前端发起面 / Quality 白名单三方共用
    /// （#1327：三方此前各写各的字面量，种子态处置审批结构性走不通）。
    /// </summary>
    public const string NcrDocumentType = ApprovalDocumentTypes.NcrDisposition;
    public const string NcrSourceService = "quality";

    /// <summary>
    /// 销售订单「信用解冻」审批模板（#1290）。与两张历史模板不同，它不挂任何历史链，
    /// 而是让 ERP <c>ReleaseSalesOrderCreditHoldCommand</c> 硬编码引用的当前流程模板开箱存在——
    /// 编码必须与 ERP 侧字面量逐字一致，因此不落在 <c>APT-WB-</c> 号段。
    /// </summary>
    public const string SalesCreditReleaseTemplateCode = ApprovalTemplateCodes.SalesCreditRelease;
    public const string SalesCreditReleaseDocumentType = "sales-order-credit-release";

    /// <summary>
    /// 盘点差异审批模板（#1344 扩修）。同样不挂历史链，只保证 Inventory
    /// <c>ConfirmStockCountAdjustmentCommand</c>（差异超阈值分支）引用的模板开箱存在——
    /// 此前发起侧默认 <c>COUNT-VARIANCE</c> 而种子无此模板，盘点确认必 400（走查台账 #66）。
    /// 审批人取厂长：账实差异有财务影响，由仓储部之外的人核准，且演示 / 走查用的就是该账号。
    /// </summary>
    public const string StockCountVarianceTemplateCode = ApprovalTemplateCodes.StockCountVariance;
    public const string StockCountVarianceDocumentType = ApprovalTemplateCodes.StockCountVarianceDocumentType;

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

    /// <summary>
    /// 计划主管（EMP-029），厂长外出期间的采购审批受托人。
    ///
    /// 序号按 MasterData <c>WorldBibleSpec.BuildEmployees()</c> 的同一公式复算：
    /// 生产部 3+6+19 = 28 人在前，计划部从 0 基序号 28 起 → <c>user-emp-029</c>
    /// （与 DemandPlanning 侧 <c>WorldHistoryPlanningSpec.PlanningSupervisor</c> 逐字一致）。
    /// </summary>
    public const string PlanningSupervisorUserId = "user-emp-029";

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

    #region 审批委托（approval.approval_delegations）

    /// <summary>
    /// 委托是**低频事件**：不随 scale 缩放，只随历史纵深增长。
    /// 首次委托落在上线后第 10 天，此后每 14 天一次（厂长出差 / 质量主管休假的固定节奏），
    /// 全量 29 周历史下约 15 条——十几条正是「一年里出差休假几次」的真实量级。
    /// </summary>
    public const int DelegationFirstDayOffset = 10;

    /// <summary>两次委托之间的间隔（自然日）。</summary>
    public const int DelegationIntervalDays = 14;

    /// <summary>历史委托中提前撤销（委托人提前回厂）的比例。</summary>
    public const double DelegationRevokedProbability = 0.25;

    /// <summary>当前仍在生效的那条委托：起始日 = <c>asOfDate - 3</c>，跨过 asOfDate 再持续一周。</summary>
    public const int CurrentDelegationStartOffsetDays = -3;
    public const int CurrentDelegationDurationDays = 10;

    /// <summary>委托期长度候选（自然日）：3 天短差 / 一周 / 十天 / 两周年休。</summary>
    private static readonly IReadOnlyList<int> DelegationDurationDays = [3, 7, 10, 14];

    private static readonly IReadOnlyList<string> PurchaseDelegationReasons =
    [
        "厂长赴长三角整车一厂技术交流，采购审批临时委托计划主管",
        "厂长外出参加供应商年度大会，期间采购审批由计划主管代行",
        "厂长休年假，采购订单审批临时授权计划主管",
    ];

    private static readonly IReadOnlyList<string> NcrDelegationReasons =
    [
        "质量主管赴客户处处理索赔，NCR 处置评审委托质量工程师",
        "质量主管参加 IATF 16949 外审培训，期间 NCR 处置由质量工程师代评",
        "质量主管休假，不合格品处置审批临时授权质量工程师",
    ];

    private static readonly IReadOnlyList<string> DelegationRevokeReasonSuffixes =
    [
        "（提前返厂，委托提前收回）",
        "（行程取消，委托作废）",
    ];

    /// <summary>
    /// 全量审批委托事实流（确定性纯函数，seed 与校验器共用）。
    ///
    /// 结构：历史委托（已过期，其中约 1/4 被提前撤销）+ 末尾一条**当前仍生效**的委托，
    /// 于是委托区块上既有 <c>active</c> 也有 <c>revoked</c>，且「现在谁在代批」讲得通。
    /// 委托人 / 受托人全部是既有审批人（厂长、质量主管、计划主管、质量工程师），不新造人物。
    /// </summary>
    public static IReadOnlyList<WorldHistoryDelegationFact> BuildDelegationFacts(DateOnly asOfDate)
    {
        var facts = new List<WorldHistoryDelegationFact>(24);
        var episode = 0;
        for (var dayOffset = DelegationFirstDayOffset; ; dayOffset += DelegationIntervalDays, episode++)
        {
            var startDay = WorldHistoryCalendar.GoLiveDate.AddDays(dayOffset);
            if (startDay > asOfDate)
            {
                break;
            }

            facts.Add(BuildHistoricalDelegationFact(episode, startDay, asOfDate));
        }

        facts.Add(BuildCurrentDelegationFact(asOfDate));
        facts.Add(BuildCurrentNcrDelegationFact(asOfDate));
        return facts;
    }

    private static WorldHistoryDelegationFact BuildHistoricalDelegationFact(int episode, DateOnly startDay, DateOnly asOfDate)
    {
        // 采购线与质量线交替：厂长出差与质量主管休假在时间轴上错开，不会同一周两条委托。
        var isPurchase = episode % 2 == 0;
        var streamKey = FormattableString.Invariant($"delegation:{(isPurchase ? "po" : "ncr")}:{episode:D3}");
        var random = new WorldHistoryRandom(streamKey);
        var durationDays = random.Pick(DelegationDurationDays);

        var effectiveFromUtc = MomentOn(ClampToHistory(startDay, asOfDate), streamKey, "delegation-start");
        var effectiveToUtc = effectiveFromUtc.AddDays(durationDays);
        var createdAtUtc = Earlier(
            MomentOn(ClampToHistory(startDay.AddDays(-1), asOfDate), streamKey, "delegation-created"),
            effectiveFromUtc);

        var delegatorUserId = isPurchase ? AdminUserId : QualitySupervisorUserId;
        var delegateUserId = isPurchase
            ? PlanningSupervisorUserId
            : Assign(QualityEngineers, streamKey).UserId;
        var reason = random.Pick(isPurchase ? PurchaseDelegationReasons : NcrDelegationReasons);

        // 提前撤销：撤销时刻落在委托期内 60% 处，且必须仍在历史窗内（否则这条只能是自然到期）。
        var revoked = random.Chance(DelegationRevokedProbability);
        DateTimeOffset? revokedAtUtc = null;
        if (revoked)
        {
            var revokeDay = ClampToHistory(startDay.AddDays(Math.Max(1, durationDays * 3 / 5)), asOfDate);
            var candidate = Later(
                MomentOn(revokeDay, streamKey, "delegation-revoke"),
                effectiveFromUtc.AddHours(2));
            revokedAtUtc = candidate < effectiveToUtc ? candidate : effectiveFromUtc.AddHours(2);
        }

        return new WorldHistoryDelegationFact(
            DelegatorUserId: delegatorUserId,
            DelegateUserId: delegateUserId,
            DocumentType: isPurchase ? PurchaseDocumentType : NcrDocumentType,
            EffectiveFromUtc: effectiveFromUtc,
            EffectiveToUtc: effectiveToUtc,
            Reason: revokedAtUtc is null ? reason : reason + random.Pick(DelegationRevokeReasonSuffixes),
            CreatedAtUtc: createdAtUtc,
            RevokedAtUtc: revokedAtUtc);
    }

    /// <summary>当前生效的那条：厂长本周外出，**不限单据类型**全部代批（<c>DocumentType = null</c> 的落点）。</summary>
    private static WorldHistoryDelegationFact BuildCurrentDelegationFact(DateOnly asOfDate)
    {
        const string streamKey = "delegation:current";
        var startDay = ClampToHistory(asOfDate.AddDays(CurrentDelegationStartOffsetDays), asOfDate);
        var effectiveFromUtc = MomentOn(startDay, streamKey, "delegation-start");
        var createdAtUtc = Earlier(
            MomentOn(ClampToHistory(startDay.AddDays(-1), asOfDate), streamKey, "delegation-created"),
            effectiveFromUtc);

        return new WorldHistoryDelegationFact(
            DelegatorUserId: AdminUserId,
            DelegateUserId: PlanningSupervisorUserId,
            DocumentType: null,
            EffectiveFromUtc: effectiveFromUtc,
            EffectiveToUtc: effectiveFromUtc.AddDays(CurrentDelegationDurationDays),
            Reason: "厂长本周赴华中商用车配套客户现场，期间全部审批事项委托计划主管代行",
            CreatedAtUtc: createdAtUtc,
            RevokedAtUtc: null);
    }

    /// <summary>
    /// 当前生效的第二条：**质量主管休假期间，NCR 处置审批委托厂长（admin）代行**。
    ///
    /// 为什么种子里必须有这一条（#1327）：NCR 处置模板的审批人是质量主管 EMP-033，
    /// 而演示 / 走查用的是厂长账号 <c>user-admin</c>——没有这条委托，种子态下任何一条
    /// NCR 处置审批链都无人可裁，处置链路在界面上走不到底。委托是审批域已有的一等机制
    /// （<c>ResolveApprovalStepCommandHandler</c> 按 delegate → delegator 匹配待办步骤），
    /// 因此这里不改模板步骤、不改历史链结构，只补一条业务上讲得通的生效中委托。
    /// </summary>
    private static WorldHistoryDelegationFact BuildCurrentNcrDelegationFact(DateOnly asOfDate)
    {
        const string streamKey = "delegation:current-ncr";
        var startDay = ClampToHistory(asOfDate.AddDays(CurrentDelegationStartOffsetDays), asOfDate);
        var effectiveFromUtc = MomentOn(startDay, streamKey, "delegation-start");
        var createdAtUtc = Earlier(
            MomentOn(ClampToHistory(startDay.AddDays(-1), asOfDate), streamKey, "delegation-created"),
            effectiveFromUtc);

        return new WorldHistoryDelegationFact(
            DelegatorUserId: QualitySupervisorUserId,
            DelegateUserId: AdminUserId,
            DocumentType: NcrDocumentType,
            EffectiveFromUtc: effectiveFromUtc,
            EffectiveToUtc: effectiveFromUtc.AddDays(CurrentDelegationDurationDays),
            Reason: "质量主管休假，期间不合格品处置审批委托厂长代行",
            CreatedAtUtc: createdAtUtc,
            RevokedAtUtc: null);
    }

    #endregion

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

    private static DateTimeOffset Earlier(DateTimeOffset candidate, DateTimeOffset ceiling) =>
        candidate < ceiling ? candidate : ceiling;
}

/// <summary>一条历史审批委托事实（委托人 / 受托人 / 单据范围 / 起止 / 中文事由 / 是否提前撤销）。</summary>
public sealed record WorldHistoryDelegationFact(
    string DelegatorUserId,
    string DelegateUserId,
    string? DocumentType,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset EffectiveToUtc,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc)
{
    /// <summary>委托人的公开 actor 引用（<c>user:user-admin</c>），与 Gateway 注入口径一致。</summary>
    public string DelegatorActorRef => DelegatorUserId;

    /// <summary>受托人的公开 actor 引用。</summary>
    public string DelegateActorRef => DelegateUserId;

    /// <summary>委托单的创建人 / 撤销人都是委托人自己（谁授权谁收回）。</summary>
    public string CreatedBy => $"{WorldHistoryApprovalSpec.ActorTypeUser}:{DelegatorUserId}";

    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>自然键：同一委托人 → 受托人 + 同一生效起点即同一条委托（幂等预查用）。</summary>
    public string NaturalKey => NaturalKeyOf(DelegatorUserId, DelegateUserId, EffectiveFromUtc);

    public static string NaturalKeyOf(string delegatorActorRef, string delegateActorRef, DateTimeOffset effectiveFromUtc) =>
        FormattableString.Invariant($"{delegatorActorRef}|{delegateActorRef}|{effectiveFromUtc.UtcDateTime:yyyyMMddHHmm}");
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
