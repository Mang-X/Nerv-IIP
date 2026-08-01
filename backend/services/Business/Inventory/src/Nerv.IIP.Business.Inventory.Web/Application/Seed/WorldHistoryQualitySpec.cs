using System.Globalization;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **二期质量域**的确定性事实流。
///
/// 三条检验来源全部**派生自已有的共享形状**，质量域不自造任何单据：
/// <list type="number">
/// <item>工序检验（<c>operation</c> / <c>mes</c>）：挂在一期 MES 工单的性能终检工序（seq 70）上；</item>
/// <item>来料检验（<c>receiving</c> / <c>wms</c>）：挂在一期 ERP 采购收货单上；</item>
/// <item>成品终检（<c>final</c> / <c>erp</c>）：挂在一期 ERP 发货单上。</item>
/// </list>
/// 因此「检验任务 → 检验记录 → NCR → 处置 → 关单」这条链上的每个源单据号，在 ERP / MES 库里
/// 都真实存在，无需跨服务查询或外键（与一期同一手法）。
///
/// 本类型是纯函数：同一 <c>(asOfDate, scale)</c> 必得同一张事实表，seed 与校验器共用它，
/// 于是「写入的东西」与「校验的东西」不可能漂移。
/// </summary>
public static class WorldHistoryQualitySpec
{
    #region 检验计划（世界观历史专用，与 IP-DEMO-* 固定演示计划隔离）

    public const string ReceivingPlanCode = "IP-WB-RCV-001";
    public const string OperationPlanCode = "IP-WB-OP-001";
    public const string FinalPlanCode = "IP-WB-FIN-001";

    /// <summary>本引擎产出的全部质量号段前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes =
    [
        "NCR-2026-", "IP-WB-", "INV-SCRAP-",
    ];

    /// <summary>三张检验计划。计划不绑定 SKU（跨 24 个成品与全部原料共用），因此 <c>SkuCode</c> 恒为空。</summary>
    public static readonly IReadOnlyList<WorldHistoryInspectionPlanDefinition> InspectionPlans =
    [
        new(ReceivingPlanCode, "来料检验", "receiving",
        [
            new("appearance", "外观检查", "visual", "minor", true, "抽检 AQL 2.5", WorldHistoryCharacteristicKind.Attribute, null, null, null, null),
            new("dimension", "关键尺寸", "caliper", "major", true, "首末件", WorldHistoryCharacteristicKind.Variable, 25m, 24.8m, 25.2m, "mm"),
            new("certificate", "材质证明", "document", "major", true, "每批", WorldHistoryCharacteristicKind.Attribute, null, null, null, null),
        ]),
        new(OperationPlanCode, "性能终检", "operation",
        [
            new("damping-force", "阻尼力", "test-bench", "major", true, "100%", WorldHistoryCharacteristicKind.Variable, 1200m, 1080m, 1320m, "N"),
            new("stroke", "行程", "caliper", "major", true, "100%", WorldHistoryCharacteristicKind.Variable, 180m, 179.5m, 180.5m, "mm"),
            new("leakage", "渗漏检查", "visual", "major", true, "100%", WorldHistoryCharacteristicKind.Attribute, null, null, null, null),
        ]),
        new(FinalPlanCode, "成品终检", "final",
        [
            new("appearance", "外观检查", "visual", "minor", true, "100%", WorldHistoryCharacteristicKind.Attribute, null, null, null, null),
            new("labeling", "标识核对", "visual", "major", true, "100%", WorldHistoryCharacteristicKind.Attribute, null, null, null, null),
            new("packaging", "包装完整性", "visual", "minor", true, "抽检", WorldHistoryCharacteristicKind.Attribute, null, null, null, null),
        ]),
    ];

    public static WorldHistoryInspectionPlanDefinition PlanFor(string sourceType) =>
        InspectionPlans.Single(x => string.Equals(x.Category, sourceType, StringComparison.Ordinal));

    #endregion

    #region 不合格率与处置分布（设定集 §7）

    /// <summary>设定集 §7 的整体不合格率 2.3%。</summary>
    public const double NonconformingRate = 0.023;

    /// <summary>
    /// 分层不合格率——**这是为了让处置分布对得上而必须的建模选择（裁决点）**。
    ///
    /// 报废处置只有在「工序检验 + 该工单一期确有投料报废量」时才合法（见 <c>BuildInspectionFacts</c> 的兜底），
    /// 而这类检验只占全部已完成检验的约 15%。若不合格件在全体检验上均匀撒落，
    /// 报废处置最多只能占到 2% 出头，与设定集的 60/25/15 相差一个量级。
    ///
    /// 现实里这两件事本来就相关：**当班产出过报废件的工单，其终检不合格率本就明显更高**。
    /// 于是把不合格率按「是否报废高发工单」分两档，加权平均仍落在 <see cref="NonconformingRate"/>：
    /// <c>0.1485 × 0.0465 + 0.8515 × 0.0189 ≈ 0.023</c>。
    /// 两档都是常数（不依赖本次生成的样本量），因此单张单据的结论与 <c>Scale</c> 无关。
    /// </summary>
    public const double ScrapProneNonconformingRate = 0.0465;

    /// <summary>非报废高发检验的不合格率（见 <see cref="ScrapProneNonconformingRate"/> 的推导）。</summary>
    public const double BaselineNonconformingRate = 0.0189;

    /// <summary>设定集 §7 的处置分布目标：返工 60 / 让步 25 / 报废 15。</summary>
    public const int ReworkDispositionShare = 60;
    public const int ConditionalReleaseDispositionShare = 25;
    public const int ScrapDispositionShare = 15;

    // 报废高发层约占 NCR 的 30%（0.1485×0.0465/0.023），在该层内按 50/35/15 抽处置，
    // 在其余 70% 内按 71/29 抽（无报废），加权后恰好落回 60/25/15。
    private static readonly IReadOnlyList<WorldHistoryInspectionDisposition> ScrapProneDispositions =
    [
        WorldHistoryInspectionDisposition.Scrap,
        WorldHistoryInspectionDisposition.Rework,
        WorldHistoryInspectionDisposition.ConditionalRelease,
    ];

    private static readonly IReadOnlyList<int> ScrapProneDispositionWeights = [50, 35, 15];

    private static readonly IReadOnlyList<WorldHistoryInspectionDisposition> BaselineDispositions =
    [
        WorldHistoryInspectionDisposition.Rework,
        WorldHistoryInspectionDisposition.ConditionalRelease,
    ];

    private static readonly IReadOnlyList<int> BaselineDispositionWeights = [71, 29];

    /// <summary>不良原因码，与 <c>QualitySeedService</c> 预置的原因码目录同码。</summary>
    private static readonly IReadOnlyList<WorldHistoryDefectReason> ReworkReasons =
    [
        new("RSN-DIMENSION", "尺寸超差"),
        new("RSN-APPEARANCE", "外观缺陷"),
        new("RSN-LABELING", "标识错误"),
    ];

    private static readonly IReadOnlyList<WorldHistoryDefectReason> ConditionalReleaseReasons =
    [
        new("RSN-PACKAGING", "包装破损"),
        new("RSN-APPEARANCE", "外观缺陷"),
    ];

    private static readonly IReadOnlyList<WorldHistoryDefectReason> ScrapReasons =
    [
        new("RSN-FUNC-FAIL", "功能失效"),
        new("RSN-CONTAMINATION", "污染异物"),
    ];

    #endregion

    /// <summary>触发幂等键前缀：校验器据此把世界观历史的检验任务与租户真实任务分开。</summary>
    public const string TriggerKeyPrefix = "seed:world-history:";

    /// <summary>检验任务的触发幂等键：源服务 + 源单据 + 行号，重复 seed 时据此跳过。</summary>
    public static string TriggerIdempotencyKey(string sourceService, string sourceDocumentId, string? sourceDocumentLineId) =>
        $"{TriggerKeyPrefix}{sourceService}:{sourceDocumentId}:{sourceDocumentLineId ?? "-"}";

    /// <summary>报废处置对应的库存移动 id——二期库存域会以同一 id 落一笔报废流水。</summary>
    public static string ScrapMovementId(string ncrCode) => $"INV-SCRAP-{ncrCode}";

    /// <summary>成品终检的批次号：发货批即该订单工单的产出批。</summary>
    public static string DeliveredLotNo(string workOrderNo) => $"LOT-{workOrderNo}";

    /// <summary>
    /// 全量质量事实流。顺序固定为「工序检验 → 来料检验 → 成品终检」，
    /// <c>NCR-2026-####</c> 即按该顺序连续编号，因此同一 <c>(asOfDate, scale)</c> 下号码稳定。
    /// </summary>
    public static IReadOnlyList<WorldHistoryInspectionFact> BuildInspectionFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale);
        var drafts = new List<InspectionDraft>(workOrderFacts.Count * 2);

        foreach (var workOrder in workOrderFacts.Where(fact => fact.HasFinalInspection))
        {
            drafts.Add(new InspectionDraft(
                SourceType: "operation",
                SourceService: "mes",
                SourceDocumentId: workOrder.Plan.WorkOrderNo,
                SourceDocumentLineId: WorldHistoryMesSpec.QualityInspectionSequence.ToString(CultureInfo.InvariantCulture),
                SkuCode: workOrder.Plan.SkuCode,
                Quantity: workOrder.Plan.WorkOrderQuantity,
                UomCode: WorldHistorySpec.UomCode,
                BatchNo: workOrder.ProducedLotNo,
                BaseDate: workOrder.Timeline.ProductionCompletionDate,
                Status: workOrder.Execution switch
                {
                    WorldHistoryExecutionDepth.Closed => WorldHistoryInspectionStatus.Completed,
                    WorldHistoryExecutionDepth.Partial => WorldHistoryInspectionStatus.InProgress,
                    _ => WorldHistoryInspectionStatus.Pending,
                },
                // 报废量上限来自一期的投料放大量：质量域判报废时不得越过它（数量对账的边界）。
                ScrapCapacity: workOrder.Plan.ScrapQuantity,
                LinkedWorkOrderNo: workOrder.Plan.WorkOrderNo));
        }

        foreach (var purchase in WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale).Where(plan => plan.IsReceived))
        {
            drafts.Add(new InspectionDraft(
                SourceType: "receiving",
                SourceService: "wms",
                SourceDocumentId: purchase.PurchaseReceiptNo,
                SourceDocumentLineId: null,
                SkuCode: purchase.SkuCode,
                Quantity: purchase.Quantity,
                UomCode: purchase.UomCode,
                BatchNo: WorldHistoryProcurementSpec.PurchasedLotNo(purchase.PurchaseOrderNo),
                BaseDate: purchase.ReceiptDate,
                Status: WorldHistoryInspectionStatus.Completed,
                ScrapCapacity: 0m,
                LinkedWorkOrderNo: null));
        }

        // #1374：出货检验在完工装箱环节成立，与发运与否无关。
        foreach (var order in WorldHistorySpec.BuildOrderPlans(asOfDate, scale).Where(plan => plan.IsProductionClosed))
        {
            drafts.Add(new InspectionDraft(
                SourceType: "final",
                SourceService: "erp",
                SourceDocumentId: WorldHistorySpec.DeliveryOrderNo(order.Index),
                SourceDocumentLineId: null,
                SkuCode: order.SkuCode,
                Quantity: order.Quantity,
                UomCode: WorldHistorySpec.UomCode,
                BatchNo: DeliveredLotNo(order.WorkOrderNo),
                BaseDate: WorldHistoryTimeline.For(order, asOfDate).ShipDate,
                Status: WorldHistoryInspectionStatus.Completed,
                ScrapCapacity: 0m,
                LinkedWorkOrderNo: order.WorkOrderNo));
        }

        var reworkPool = ResolveReworkWorkOrderPool(workOrderFacts);
        var facts = new List<WorldHistoryInspectionFact>(drafts.Count);
        var ncrSequence = 0;
        var index = 0;
        foreach (var draft in drafts)
        {
            index++;
            facts.Add(BuildFact(draft, index, asOfDate, reworkPool, ref ncrSequence));
        }

        return facts;
    }

    /// <summary>
    /// 返工处置关单时引用的补产工单池：优先用一期真实存在的 <c>WO-2026-R####</c>；
    /// 若本次规模小到没有补产工单，退回全部订单工单（同样真实存在），最后才退回本次检验的源工单。
    /// </summary>
    private static IReadOnlyList<string> ResolveReworkWorkOrderPool(IReadOnlyList<WorldHistoryWorkOrderFact> workOrderFacts)
    {
        var rework = workOrderFacts.Where(fact => fact.IsRework).Select(fact => fact.Plan.WorkOrderNo).ToArray();
        return rework.Length > 0
            ? rework
            : [.. workOrderFacts.Select(fact => fact.Plan.WorkOrderNo)];
    }

    private static WorldHistoryInspectionFact BuildFact(
        InspectionDraft draft,
        int index,
        DateOnly asOfDate,
        IReadOnlyList<string> reworkPool,
        ref int ncrSequence)
    {
        var plan = PlanFor(draft.SourceType);
        var triggerKey = TriggerIdempotencyKey(draft.SourceService, draft.SourceDocumentId, draft.SourceDocumentLineId);
        var random = new WorldHistoryRandom($"inspection:{triggerKey}");

        var day0 = ClampToHistory(draft.BaseDate, asOfDate);
        var createdAtUtc = WorldHistoryPhase2Spec.MomentOn(day0, triggerKey, "inspection-created");
        var startedAtUtc = createdAtUtc.AddMinutes(random.NextInt(20, 91));
        var completedAtUtc = startedAtUtc.AddMinutes(random.NextInt(20, 121));
        var inspector = WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.Inspectors, triggerKey);

        // 报废只在「工序检验 + 该工单一期确有投料报废量」时合法；这一层同时是不合格率的高发层。
        var scrapProne = draft.ScrapCapacity > 0m && string.Equals(draft.SourceType, "operation", StringComparison.Ordinal);
        var nonconforming = draft.Status == WorldHistoryInspectionStatus.Completed
            && random.Chance(scrapProne ? ScrapProneNonconformingRate : BaselineNonconformingRate);

        if (!nonconforming)
        {
            return new WorldHistoryInspectionFact(
                Index: index,
                PlanCode: plan.PlanCode,
                SourceType: draft.SourceType,
                SourceService: draft.SourceService,
                SourceDocumentId: draft.SourceDocumentId,
                SourceDocumentLineId: draft.SourceDocumentLineId,
                SkuCode: draft.SkuCode,
                Quantity: draft.Quantity,
                UomCode: draft.UomCode,
                BatchNo: draft.BatchNo,
                TriggerIdempotencyKey: triggerKey,
                Status: draft.Status,
                InspectorUserId: inspector.UserId,
                CreatedAtUtc: createdAtUtc,
                DueAtUtc: createdAtUtc.AddHours(8),
                StartedAtUtc: draft.Status == WorldHistoryInspectionStatus.Pending ? null : startedAtUtc,
                CompletedAtUtc: draft.Status == WorldHistoryInspectionStatus.Completed ? completedAtUtc : null,
                Disposition: WorldHistoryInspectionDisposition.None,
                DefectCharacteristicCode: null,
                DefectQuantity: 0m,
                DefectReasonCode: null,
                DefectReasonText: null,
                NcrCode: null,
                NcrOpenedAtUtc: null,
                NcrDispositionAtUtc: null,
                NcrClosedAtUtc: null,
                MrbReviewerUserId: null,
                ReworkWorkOrderNo: null,
                ScrapMovementId: null,
                ReinspectedAtUtc: null);
        }

        var disposition = scrapProne
            ? random.PickWeighted(ScrapProneDispositions, ScrapProneDispositionWeights)
            : random.PickWeighted(BaselineDispositions, BaselineDispositionWeights);

        // 兜底不变量：报废量必须落在一期工单的投料放大量之内，
        // 否则「质量报废 ↔ MES 投料放大」这条对账线会对不上——不合法就退回返工。
        if (disposition == WorldHistoryInspectionDisposition.Scrap && !scrapProne)
        {
            disposition = WorldHistoryInspectionDisposition.Rework;
        }

        var defectQuantity = ResolveDefectQuantity(random, draft, disposition);
        var reason = random.Pick(disposition switch
        {
            WorldHistoryInspectionDisposition.Scrap => ScrapReasons,
            WorldHistoryInspectionDisposition.ConditionalRelease => ConditionalReleaseReasons,
            _ => ReworkReasons,
        });
        var defectCharacteristic = random.Pick(plan.Characteristics).Code;

        ncrSequence++;
        var ncrCode = WorldHistoryPhase2Spec.NonconformanceReportNo(ncrSequence);
        var openedAtUtc = completedAtUtc.AddMinutes(15);
        var dispositionAtUtc = Later(
            WorldHistoryPhase2Spec.MomentOn(ClampToHistory(WorldHistoryCalendar.AddWorkingDays(day0, 1), asOfDate), ncrCode, "ncr-disposition"),
            openedAtUtc.AddMinutes(30));
        var reinspectedAtUtc = disposition == WorldHistoryInspectionDisposition.Rework
            ? Later(
                WorldHistoryPhase2Spec.MomentOn(ClampToHistory(WorldHistoryCalendar.AddWorkingDays(day0, 2), asOfDate), ncrCode, "reinspection"),
                dispositionAtUtc.AddMinutes(30))
            : (DateTimeOffset?)null;
        var closedAtUtc = Later(
            WorldHistoryPhase2Spec.MomentOn(ClampToHistory(WorldHistoryCalendar.AddWorkingDays(day0, 3), asOfDate), ncrCode, "ncr-closed"),
            (reinspectedAtUtc ?? dispositionAtUtc).AddMinutes(30));

        return new WorldHistoryInspectionFact(
            Index: index,
            PlanCode: plan.PlanCode,
            SourceType: draft.SourceType,
            SourceService: draft.SourceService,
            SourceDocumentId: draft.SourceDocumentId,
            SourceDocumentLineId: draft.SourceDocumentLineId,
            SkuCode: draft.SkuCode,
            Quantity: draft.Quantity,
            UomCode: draft.UomCode,
            BatchNo: draft.BatchNo,
            TriggerIdempotencyKey: triggerKey,
            Status: draft.Status,
            InspectorUserId: inspector.UserId,
            CreatedAtUtc: createdAtUtc,
            DueAtUtc: createdAtUtc.AddHours(8),
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            Disposition: disposition,
            DefectCharacteristicCode: defectCharacteristic,
            DefectQuantity: defectQuantity,
            DefectReasonCode: reason.ReasonCode,
            DefectReasonText: reason.ReasonName,
            NcrCode: ncrCode,
            NcrOpenedAtUtc: openedAtUtc,
            NcrDispositionAtUtc: dispositionAtUtc,
            NcrClosedAtUtc: closedAtUtc,
            MrbReviewerUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.QualityEngineers, ncrCode).UserId,
            ReworkWorkOrderNo: disposition == WorldHistoryInspectionDisposition.Rework
                ? ResolveReworkWorkOrderNo(reworkPool, draft, ncrCode)
                : null,
            ScrapMovementId: disposition == WorldHistoryInspectionDisposition.Scrap ? ScrapMovementId(ncrCode) : null,
            ReinspectedAtUtc: reinspectedAtUtc);
    }

    /// <summary>不良数量取受检量的 1%–3%（至少 1 件）；报废另受一期投料放大量的硬上限约束。</summary>
    private static decimal ResolveDefectQuantity(
        WorldHistoryRandom random,
        InspectionDraft draft,
        WorldHistoryInspectionDisposition disposition)
    {
        var share = random.NextInt(1, 4) / 100m;
        var defect = Math.Max(1m, decimal.Round(draft.Quantity * share, 0, MidpointRounding.AwayFromZero));
        if (disposition == WorldHistoryInspectionDisposition.Scrap)
        {
            defect = Math.Min(defect, draft.ScrapCapacity);
        }

        return Math.Max(1m, Math.Min(defect, draft.Quantity));
    }

    private static string ResolveReworkWorkOrderNo(
        IReadOnlyList<string> reworkPool,
        InspectionDraft draft,
        string ncrCode)
    {
        if (reworkPool.Count > 0)
        {
            return new WorldHistoryRandom($"rework-reference:{ncrCode}").Pick(reworkPool);
        }

        return draft.LinkedWorkOrderNo ?? draft.SourceDocumentId;
    }

    /// <summary>把候选日期夹进 <c>[上线日, asOfDate]</c> 并回退到工作日（周日停产，历史里不得出现周日事件）。</summary>
    public static DateOnly ClampToHistory(DateOnly candidate, DateOnly asOfDate)
    {
        var cursor = candidate > asOfDate ? asOfDate : candidate;
        while (!WorldHistoryCalendar.IsWorkingDay(cursor) && cursor > WorldHistoryCalendar.GoLiveDate)
        {
            cursor = cursor.AddDays(-1);
        }

        return cursor;
    }

    private static DateTimeOffset Later(DateTimeOffset candidate, DateTimeOffset floor) =>
        candidate > floor ? candidate : floor;

    private sealed record InspectionDraft(
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string? SourceDocumentLineId,
        string SkuCode,
        decimal Quantity,
        string UomCode,
        string BatchNo,
        DateOnly BaseDate,
        WorldHistoryInspectionStatus Status,
        decimal ScrapCapacity,
        string? LinkedWorkOrderNo);
}

/// <summary>检验特性类型（与领域层 <c>InspectionCharacteristicTypes</c> 同字面量，规格层不反向依赖领域层）。</summary>
public static class WorldHistoryCharacteristicKind
{
    public const string Variable = "variable";
    public const string Attribute = "attribute";
}

public sealed record WorldHistoryDefectReason(string ReasonCode, string ReasonName);

public sealed record WorldHistoryInspectionCharacteristic(
    string Code,
    string Name,
    string Method,
    string Severity,
    bool Required,
    string SamplingRule,
    string CharacteristicType,
    decimal? NominalValue,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    string? UnitCode)
{
    public bool IsVariable => string.Equals(CharacteristicType, WorldHistoryCharacteristicKind.Variable, StringComparison.Ordinal);
}

public sealed record WorldHistoryInspectionPlanDefinition(
    string PlanCode,
    string PlanName,
    string Category,
    IReadOnlyList<WorldHistoryInspectionCharacteristic> Characteristics);

/// <summary>检验任务在历史里的落点状态。</summary>
public enum WorldHistoryInspectionStatus
{
    /// <summary>待检：源工单已下达但尚未开工，检验任务排队。</summary>
    Pending,

    /// <summary>检验中：源工单在制，检验员已领取任务。</summary>
    InProgress,

    /// <summary>已完成：有检验记录，可能带 NCR。</summary>
    Completed,
}

/// <summary>NCR 处置类型（设定集 §7 返工 / 让步 / 报废）。</summary>
public enum WorldHistoryInspectionDisposition
{
    /// <summary>合格，无 NCR。</summary>
    None,

    /// <summary>返工：判定 rejected，关单引用补产工单。</summary>
    Rework,

    /// <summary>让步接收：判定 conditional-release，按让步数量关单。</summary>
    ConditionalRelease,

    /// <summary>报废：判定 rejected，关单引用库存报废流水 id。</summary>
    Scrap,
}

/// <summary>一条历史检验事实（任务 + 记录 + 可选 NCR 全链）。</summary>
public sealed record WorldHistoryInspectionFact(
    int Index,
    string PlanCode,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string SkuCode,
    decimal Quantity,
    string UomCode,
    string BatchNo,
    string TriggerIdempotencyKey,
    WorldHistoryInspectionStatus Status,
    string InspectorUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DueAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    WorldHistoryInspectionDisposition Disposition,
    string? DefectCharacteristicCode,
    decimal DefectQuantity,
    string? DefectReasonCode,
    string? DefectReasonText,
    string? NcrCode,
    DateTimeOffset? NcrOpenedAtUtc,
    DateTimeOffset? NcrDispositionAtUtc,
    DateTimeOffset? NcrClosedAtUtc,
    string? MrbReviewerUserId,
    string? ReworkWorkOrderNo,
    string? ScrapMovementId,
    DateTimeOffset? ReinspectedAtUtc)
{
    /// <summary>是否有检验记录（只有已完成的任务才有）。</summary>
    public bool HasRecord => Status == WorldHistoryInspectionStatus.Completed;

    /// <summary>是否开了 NCR。</summary>
    public bool HasNonconformance => Disposition != WorldHistoryInspectionDisposition.None;

    /// <summary>检验记录判定（passed / rejected / conditional-release）。</summary>
    public string RecordResult => Disposition switch
    {
        WorldHistoryInspectionDisposition.None => "passed",
        WorldHistoryInspectionDisposition.ConditionalRelease => "conditional-release",
        _ => "rejected",
    };

    /// <summary>NCR 处置类型的领域取值。</summary>
    public string? DispositionType => Disposition switch
    {
        WorldHistoryInspectionDisposition.Rework => "rework",
        WorldHistoryInspectionDisposition.ConditionalRelease => "conditional-release",
        WorldHistoryInspectionDisposition.Scrap => "scrap",
        _ => null,
    };

    /// <summary>处置证据附件（让步/分选处置在领域层强制要求证据，历史链路必须带上）。</summary>
    public string AttachmentFileId => $"file-wb-{SourceDocumentId}";
}
