using System.Globalization;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **二期库存域**的确定性移动流水。
///
/// 每一笔流水都**派生自已有的共享形状**，库存域不自造任何业务事实：
/// <list type="number">
/// <item>期初库存：上线日一次性建账，数量按本区间的实际耗用量放大 <see cref="OpeningHeadroom"/> 倍取整，
///       保证任何时刻的现存量都不会为负（<c>StockLedger.ApplyMovement</c> 是硬拒绝的）；</item>
/// <item>采购收货：一期 ERP 的 <c>PR-2026-####</c> → 暂存待检 → 检验合格状态转移 → 上架进常驻库位；</item>
/// <item>领料：一期 MES 的 <c>MIR-{工单}-{序号}</c> → 常驻库位出库 → 线边库入库；已完工的工单在完工时刻倒冲线边库；</item>
/// <item>完工入库：一期 MES 的 <c>FGR-{工单}</c>，落在 MES <c>MarkPosted</c> 时写下的同一个移动 id <c>INV-{工单}</c>；</item>
/// <item>发货出库：一期 ERP 的 <c>DO-2026-#####</c>，按产出批 <c>LOT-{工单}</c> 逐批发走；</item>
/// <item>不合格品持有痕迹：二期质量域的 <c>NCR-2026-####</c>，隔离入库 → 状态转移持有 → 报废调整 / 解除放行。</item>
/// </list>
///
/// 本类型是纯函数：同一 <c>(asOfDate, scale)</c> 必得同一张流水表，seed 与校验器共用它，
/// 于是「写入的东西」与「校验的东西」不可能漂移。
///
/// <para>
/// 裁决点一 · **批次维度只承载有物理来源的批**。MES 一期的领料单上带着
/// <c>LOT-{组件}-{工单}</c>——那是车间的投料批号，一张工单一个。若库存台账照单全收，
/// 全量下要为约 1.9 万张领料单各建一条「期初批」，既不真实（期初库存不可能按未来工单分批），
/// 也会把台账批次数推到万级。因此库存侧的批次维度只有三类：期初批
/// <c>LOT-OPENING-{物料}</c>、采购批 <c>LOT-{采购单}</c>、产出批 <c>LOT-{工单}</c>；
/// 车间投料批号仍留在 MES 库里，两侧靠领料单号 <c>MIR-*</c> 对账而不是靠批号。
/// </para>
/// <para>
/// 裁决点二 · **完工入库的 <c>INV-{工单}</c> 同时落在幂等键与源单据行号上**。
/// 一期 MES 的完工入库请求以该字符串标记「已过账」，库存侧用同一字符串既做幂等键
/// （唯一索引 org+env+源服务+源单据+幂等键，重复 seed 必然命中）又做源单据行号
/// （便于从 MES 的 <c>InventoryMovementId</c> 直接反查这笔流水），无需跨库外键。
/// </para>
/// <para>
/// 裁决点三 · **线边库倒冲**。设定集里领料是「出常驻库位 → 入线边库」两笔，
/// 若只写这两笔，线边库会累积全年领料量（百万级），库存页面会明显失真。
/// 因此已完工的工单在完工时刻按组件汇总倒冲一笔线边库出库；在制工单不倒冲，
/// 其线边库结存正是「在制品」的真实含义。
/// </para>
/// </summary>
public static class WorldHistoryInventorySpec
{
    /// <summary>本引擎写入流水时使用的源服务名（与租户真实流水、固定演示事实的 <c>leader-demo-seed</c> 隔离）。</summary>
    public const string SourceService = "seed:world-history";

    /// <summary>历史库存全部为自有库存。</summary>
    public const string OwnerType = "company";

    /// <summary>期初建账的余量倍数：覆盖本区间实际耗用量的 1.2 倍，现存量因此恒为正。</summary>
    public const decimal OpeningHeadroom = 1.2m;

    #region 号段与批次

    /// <summary>期初建账单据号。</summary>
    public static string OpeningDocumentNo(int ordinal) => $"OPEN-2026-{ordinal:D4}";

    /// <summary>期初批次号：一个物料一个期初批。</summary>
    public static string OpeningLotNo(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        return $"LOT-OPENING-{skuCode}";
    }

    /// <summary>本引擎产出的全部库存号段前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes =
    [
        "OPEN-2026-", "PR-2026-", "MIR-", "FGR-", "DO-2026-", "NCR-2026-", "LOT-", "INV-",
    ];

    #endregion

    #region 移动用途（幂等键的第二段，同一源单据下唯一）

    public const string OpeningPurpose = "opening";
    public const string ReceiptInPurpose = "receipt-in";
    public const string QualityReleaseOutPurpose = "qc-release-out";
    public const string QualityReleaseInPurpose = "qc-release-in";
    public const string PutawayOutPurpose = "putaway-out";
    public const string PutawayInPurpose = "putaway-in";
    public const string MaterialIssueOutPurpose = "issue-out";
    public const string MaterialIssueInPurpose = "issue-in";
    public const string BackflushPurpose = "backflush";
    public const string FinishedGoodsInPurpose = "fg-receipt";
    public const string DeliveryOutPurpose = "delivery-out";
    public const string QuarantineInPurpose = "quarantine-in";
    public const string HoldOutPurpose = "hold-out";
    public const string HoldInPurpose = "hold-in";
    public const string HoldReleaseOutPurpose = "hold-release-out";
    public const string HoldReleaseInPurpose = "hold-release-in";
    public const string HoldReturnOutPurpose = "hold-return-out";
    public const string HoldReturnInPurpose = "hold-return-in";
    public const string ScrapAdjustmentPurpose = "scrap-adjustment";

    /// <summary>状态转移的「施加」侧用途，校验器据此核对持有痕迹成对。</summary>
    public static readonly string[] StatusTransferOutPurposes =
        [QualityReleaseOutPurpose, HoldOutPurpose, HoldReleaseOutPurpose];

    /// <summary>状态转移的「释放」侧用途。</summary>
    public static readonly string[] StatusTransferInPurposes =
        [QualityReleaseInPurpose, HoldInPurpose, HoldReleaseInPurpose];

    #endregion

    #region 移动类型（与 <c>StockMovement.SupportedMovementTypes</c> 同字面量，规格层不反向依赖领域层）

    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Transfer = "transfer";
    public const string Adjustment = "adjustment";
    public const string StatusTransferOut = "status-transfer-out";
    public const string StatusTransferIn = "status-transfer-in";

    #endregion

    #region 质量状态（与 <c>StockQualityStatus</c> 同字面量）

    public const string Unrestricted = "unrestricted";
    public const string QualityInspection = "quality";
    public const string Restricted = "restricted";
    public const string Blocked = "blocked";

    #endregion

    /// <summary>
    /// 单位成本：纯函数，同一物料在期初、采购、完工三条入库线上必须给出同一口径，
    /// 否则移动加权平均成本会随生成顺序抖动。成品按售价的 72% 折成本，其余按品类给带宽。
    /// </summary>
    public static decimal UnitCostFor(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        if (skuCode.StartsWith("FG-", StringComparison.Ordinal))
        {
            return decimal.Round(WorldHistorySpec.UnitPrice(skuCode) * 0.72m, 2);
        }

        var random = new WorldHistoryRandom($"unit-cost:{skuCode}");
        var (min, max) = skuCode[..3] switch
        {
            "SF-" => (40, 130),
            "PK-" => (2, 10),
            _ => (5, 40),
        };

        return min + (random.NextInt(0, 21) * (max - min) / 20m);
    }

    /// <summary>
    /// 全量库存移动流水，已按「时间 → 生成序」全局排好序。
    ///
    /// 排序是硬要求而非美化：<c>StockLedger.ApplyMovement</c> 会拒绝让现存量为负的流水，
    /// 因此写入必须严格按业务时间推进，否则「先发货后完工」这类倒序会直接让 seed 失败。
    /// </summary>
    public static IReadOnlyList<WorldHistoryStockMovementFact> BuildMovements(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var drafts = new List<MovementDraft>(4096);
        var openingDemand = new Dictionary<string, WorldHistoryOpeningDemand>(StringComparer.Ordinal);

        AppendPurchaseMovements(asOfDate, scale, drafts);
        var finishedGoodsMoments = AppendProductionMovements(asOfDate, scale, drafts, openingDemand);
        AppendDeliveryMovements(asOfDate, scale, drafts, finishedGoodsMoments);
        AppendNonconformanceMovements(asOfDate, scale, drafts);

        var openings = BuildOpeningMovements(openingDemand);

        // 期初排在最前：与其余流水同刻时靠生成序决胜，保证建账永远先于第一笔耗用。
        var ordered = openings
            .Concat(drafts)
            .Select((draft, ordinal) => (draft, ordinal))
            .OrderBy(item => item.draft.PostedAtUtc)
            .ThenBy(item => item.ordinal)
            .ToArray();

        var facts = new List<WorldHistoryStockMovementFact>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var draft = ordered[index].draft;
            facts.Add(new WorldHistoryStockMovementFact(
                Sequence: index + 1,
                Purpose: draft.Purpose,
                MovementType: draft.MovementType,
                SourceDocumentId: draft.SourceDocumentId,
                SourceDocumentLineId: draft.SourceDocumentLineId,
                IdempotencyKey: draft.IdempotencyKey,
                SkuCode: draft.SkuCode,
                UomCode: draft.UomCode,
                LocationCode: draft.LocationCode,
                LotNo: draft.LotNo,
                QualityStatus: draft.QualityStatus,
                Quantity: draft.Quantity,
                UnitCost: draft.UnitCost,
                PostedAtUtc: draft.PostedAtUtc));
        }

        return facts;
    }

    #region 期初建账

    private static List<MovementDraft> BuildOpeningMovements(
        Dictionary<string, WorldHistoryOpeningDemand> openingDemand)
    {
        var openingMoment = WorldHistoryCalendar.ShiftMoment(WorldHistoryCalendar.GoLiveDate, 0, 0);
        var openings = new List<MovementDraft>(openingDemand.Count);
        var ordinal = 0;
        foreach (var (skuCode, demand) in openingDemand.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            ordinal++;
            var documentNo = OpeningDocumentNo(ordinal);
            openings.Add(new MovementDraft(
                Purpose: OpeningPurpose,
                MovementType: Inbound,
                SourceDocumentId: documentNo,
                SourceDocumentLineId: "10",
                IdempotencyKey: WorldHistoryPhase2Spec.MovementKey(documentNo, OpeningPurpose),
                SkuCode: skuCode,
                // 单位取自实际耗用它的领料明细，绝不另算一套，否则台账维度会分裂成两条。
                UomCode: demand.UomCode,
                LocationCode: WorldHistoryPhase2Spec.StorageLocationFor(skuCode),
                LotNo: OpeningLotNo(skuCode),
                QualityStatus: Unrestricted,
                Quantity: OpeningQuantity(demand.Quantity),
                UnitCost: UnitCostFor(skuCode),
                PostedAtUtc: openingMoment));
        }

        return openings;
    }

    /// <summary>期初数量：耗用量放大 1.2 倍后向上取到「像人盘的」步长（万级取千、其余取百）。</summary>
    public static decimal OpeningQuantity(decimal consumedQuantity)
    {
        var target = consumedQuantity * OpeningHeadroom;
        var step = target >= 10_000m ? 1_000m : 100m;
        return Math.Max(step, Math.Ceiling(target / step) * step);
    }

    #endregion

    #region 采购收货 → 待检 → 上架

    private static void AppendPurchaseMovements(DateOnly asOfDate, double scale, List<MovementDraft> drafts)
    {
        foreach (var purchase in WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale)
                     .Where(plan => plan.IsReceived))
        {
            var receiptNo = purchase.PurchaseReceiptNo;
            var lotNo = WorldHistoryProcurementSpec.PurchasedLotNo(purchase.PurchaseOrderNo);
            var storageLocation = WorldHistoryPhase2Spec.StorageLocationFor(purchase.SkuCode);
            var receiptDay = WorldHistoryQualitySpec.ClampToHistory(purchase.ReceiptDate, asOfDate);
            var releaseDay = WorldHistoryQualitySpec.ClampToHistory(
                WorldHistoryCalendar.AddWorkingDays(receiptDay, 1), asOfDate);

            var receivedAtUtc = WorldHistoryPhase2Spec.MomentOn(receiptDay, receiptNo, "stock-receipt");
            var releasedAtUtc = Later(
                WorldHistoryPhase2Spec.MomentOn(releaseDay, receiptNo, "stock-qc-release"),
                receivedAtUtc.AddMinutes(60));
            var putawayAtUtc = releasedAtUtc.AddMinutes(45);

            // 1) 收货入暂存区，质量状态 quality（待检）——来料检验尚未出结论前不得投产。
            drafts.Add(Purchase(purchase, ReceiptInPurpose, Inbound, WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                QualityInspection, purchase.Quantity, purchase.UnitPrice, receivedAtUtc, lotNo));

            // 2) 来料检验合格：暂存区内的状态转移，quality → unrestricted。
            drafts.Add(Purchase(purchase, QualityReleaseOutPurpose, StatusTransferOut, WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                QualityInspection, -purchase.Quantity, null, releasedAtUtc, lotNo));
            drafts.Add(Purchase(purchase, QualityReleaseInPurpose, StatusTransferIn, WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                Unrestricted, purchase.Quantity, purchase.UnitPrice, releasedAtUtc, lotNo));

            // 3) 上架：暂存区 → 常驻库位。
            drafts.Add(Purchase(purchase, PutawayOutPurpose, Transfer, WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                Unrestricted, -purchase.Quantity, null, putawayAtUtc, lotNo));
            drafts.Add(Purchase(purchase, PutawayInPurpose, Transfer, storageLocation,
                Unrestricted, purchase.Quantity, purchase.UnitPrice, putawayAtUtc, lotNo));
        }
    }

    private static MovementDraft Purchase(
        WorldHistoryPurchasePlan purchase,
        string purpose,
        string movementType,
        string locationCode,
        string qualityStatus,
        decimal quantity,
        decimal? unitCost,
        DateTimeOffset postedAtUtc,
        string lotNo) =>
        new(
            Purpose: purpose,
            MovementType: movementType,
            SourceDocumentId: purchase.PurchaseReceiptNo,
            SourceDocumentLineId: "10",
            IdempotencyKey: WorldHistoryPhase2Spec.MovementKey(purchase.PurchaseReceiptNo, purpose),
            SkuCode: purchase.SkuCode,
            UomCode: purchase.UomCode,
            LocationCode: locationCode,
            LotNo: lotNo,
            QualityStatus: qualityStatus,
            Quantity: quantity,
            UnitCost: unitCost,
            PostedAtUtc: postedAtUtc);

    #endregion

    #region 领料 → 线边库 → 倒冲 → 完工入库

    /// <summary>写生产侧流水，并返回「工单号 → 完工入库时刻」供发货侧保证时序单调。</summary>
    private static Dictionary<string, DateTimeOffset> AppendProductionMovements(
        DateOnly asOfDate,
        double scale,
        List<MovementDraft> drafts,
        Dictionary<string, WorldHistoryOpeningDemand> openingDemand)
    {
        var finishedGoodsMoments = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var fact in WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale))
        {
            var issues = WorldHistoryPhase2Spec.MaterialIssues(fact);
            var consumedByComponent = new Dictionary<string, WorldHistoryOpeningDemand>(StringComparer.Ordinal);
            var lastIssueMoment = DateTimeOffset.MinValue;

            foreach (var issue in issues)
            {
                var lotNo = OpeningLotNo(issue.SkuCode);
                var storageLocation = WorldHistoryPhase2Spec.StorageLocationFor(issue.SkuCode);
                var issuedAtUtc = WorldHistoryPhase2Spec.MomentOn(
                    WorldHistoryQualitySpec.ClampToHistory(issue.IssueDate, asOfDate), issue.RequestNo, "stock-issue");

                drafts.Add(new MovementDraft(
                    MaterialIssueOutPurpose, Outbound, issue.RequestNo, "10",
                    WorldHistoryPhase2Spec.MovementKey(issue.RequestNo, MaterialIssueOutPurpose),
                    issue.SkuCode, issue.UomCode, storageLocation, lotNo, Unrestricted,
                    -issue.Quantity, null, issuedAtUtc));
                drafts.Add(new MovementDraft(
                    MaterialIssueInPurpose, Inbound, issue.RequestNo, "10",
                    WorldHistoryPhase2Spec.MovementKey(issue.RequestNo, MaterialIssueInPurpose),
                    issue.SkuCode, issue.UomCode, WorldHistoryPhase2Spec.LineSideLocationCode, lotNo, Unrestricted,
                    issue.Quantity, UnitCostFor(issue.SkuCode), issuedAtUtc));

                openingDemand[issue.SkuCode] = Accumulate(openingDemand, issue.SkuCode, issue.UomCode, issue.Quantity);
                consumedByComponent[issue.SkuCode] = Accumulate(consumedByComponent, issue.SkuCode, issue.UomCode, issue.Quantity);
                lastIssueMoment = Later(issuedAtUtc, lastIssueMoment);
            }

            if (!fact.HasFinishedGoodsReceipt)
            {
                // 在制工单不倒冲：线边库的结存就是在制品，符合车间实际。
                continue;
            }

            var completionDay = WorldHistoryQualitySpec.ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate);
            var receiptNo = fact.FinishedGoodsReceiptNo;

            // 倒冲：完工时刻按组件汇总扣减线边库，避免线边库累积全年领料量。
            foreach (var (componentSku, consumed) in consumedByComponent.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var purpose = $"{BackflushPurpose}:{componentSku}";
                var backflushAtUtc = Later(
                    WorldHistoryPhase2Spec.MomentOn(completionDay, $"{receiptNo}:{componentSku}", "stock-backflush"),
                    lastIssueMoment.AddMinutes(30));
                drafts.Add(new MovementDraft(
                    purpose, Outbound, receiptNo, componentSku,
                    WorldHistoryPhase2Spec.MovementKey(receiptNo, purpose),
                    componentSku, consumed.UomCode, WorldHistoryPhase2Spec.LineSideLocationCode,
                    OpeningLotNo(componentSku), Unrestricted, -consumed.Quantity, null, backflushAtUtc));
            }

            // 完工入库：幂等键与源单据行号都取 MES 一期写下的 INV-{工单}。
            var finishedGoodsAtUtc = Later(
                WorldHistoryPhase2Spec.MomentOn(completionDay, receiptNo, "stock-fg-receipt"),
                lastIssueMoment.AddMinutes(45));
            finishedGoodsMoments[fact.Plan.WorkOrderNo] = finishedGoodsAtUtc;
            drafts.Add(new MovementDraft(
                FinishedGoodsInPurpose, Inbound, receiptNo, fact.FinishedGoodsMovementId,
                fact.FinishedGoodsMovementId,
                fact.Plan.SkuCode, WorldHistorySpec.UomCode, WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                fact.ProducedLotNo, Unrestricted, fact.Plan.GoodQuantity, UnitCostFor(fact.Plan.SkuCode),
                finishedGoodsAtUtc));
        }

        return finishedGoodsMoments;
    }

    #endregion

    #region 发货出库

    private static void AppendDeliveryMovements(
        DateOnly asOfDate,
        double scale,
        List<MovementDraft> drafts,
        Dictionary<string, DateTimeOffset> finishedGoodsMoments)
    {
        foreach (var order in WorldHistorySpec.BuildOrderPlans(asOfDate, scale).Where(plan => plan.HasDelivery))
        {
            var deliveryOrderNo = WorldHistorySpec.DeliveryOrderNo(order.Index);
            var shipDay = WorldHistoryQualitySpec.ClampToHistory(
                WorldHistoryTimeline.For(order, asOfDate).ShipDate, asOfDate);
            var shippedAtUtc = WorldHistoryPhase2Spec.MomentOn(shipDay, deliveryOrderNo, "stock-delivery");
            if (finishedGoodsMoments.TryGetValue(order.WorkOrderNo, out var finishedGoodsAtUtc))
            {
                // 发货必须晚于完工入库：同一产出批上的顺序颠倒会直接把现存量打负。
                shippedAtUtc = Later(shippedAtUtc, finishedGoodsAtUtc.AddMinutes(60));
            }

            drafts.Add(new MovementDraft(
                DeliveryOutPurpose, Outbound, deliveryOrderNo, "10",
                WorldHistoryPhase2Spec.MovementKey(deliveryOrderNo, DeliveryOutPurpose),
                order.SkuCode, WorldHistorySpec.UomCode, WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                WorldHistoryMesSpec.ProducedLotNo(order.WorkOrderNo), Unrestricted,
                -order.Quantity, null, shippedAtUtc));
        }
    }

    #endregion

    #region 不合格品持有痕迹（镜像二期质量域的 NCR）

    /// <summary>
    /// NCR 的库存侧全链：隔离入库 → 判定持有（状态转移）→ 报废调整 / 解除放行并退回常驻库位。
    ///
    /// 隔离区 <c>WH-WB-QC-01</c> 的每条链都收敛回 0：报废由调整流水扣掉，返工与让步由解除放行退回，
    /// 因此「施加 / 释放」成对，且隔离区不会留下无解释的结存。
    /// </summary>
    private static void AppendNonconformanceMovements(DateOnly asOfDate, double scale, List<MovementDraft> drafts)
    {
        foreach (var inspection in WorldHistoryQualitySpec.BuildInspectionFacts(asOfDate, scale)
                     .Where(fact => fact.HasNonconformance))
        {
            var ncrCode = inspection.NcrCode!;
            var quantity = inspection.DefectQuantity;
            var holdLocation = WorldHistoryPhase2Spec.QualityHoldLocationCode;
            var holdStatus = inspection.Disposition == WorldHistoryInspectionDisposition.ConditionalRelease
                ? Restricted
                : Blocked;
            var openedAtUtc = inspection.NcrOpenedAtUtc!.Value;
            var decidedAtUtc = inspection.NcrDispositionAtUtc!.Value;
            var closedAtUtc = inspection.NcrClosedAtUtc!.Value;

            drafts.Add(Nonconformance(inspection, QuarantineInPurpose, Inbound, holdLocation, QualityInspection,
                quantity, UnitCostFor(inspection.SkuCode), openedAtUtc));
            drafts.Add(Nonconformance(inspection, HoldOutPurpose, StatusTransferOut, holdLocation, QualityInspection,
                -quantity, null, decidedAtUtc));
            drafts.Add(Nonconformance(inspection, HoldInPurpose, StatusTransferIn, holdLocation, holdStatus,
                quantity, UnitCostFor(inspection.SkuCode), decidedAtUtc));

            if (inspection.Disposition == WorldHistoryInspectionDisposition.Scrap)
            {
                // 报废关单：源单据行号与幂等键都取质量域关单时写下的 INV-SCRAP-{NCR}。
                var scrapMovementId = WorldHistoryQualitySpec.ScrapMovementId(ncrCode);
                drafts.Add(new MovementDraft(
                    ScrapAdjustmentPurpose, Adjustment, ncrCode, scrapMovementId, scrapMovementId,
                    inspection.SkuCode, inspection.UomCode, holdLocation, inspection.BatchNo, holdStatus,
                    -quantity, null, closedAtUtc));
                continue;
            }

            drafts.Add(Nonconformance(inspection, HoldReleaseOutPurpose, StatusTransferOut, holdLocation, holdStatus,
                -quantity, null, closedAtUtc));
            drafts.Add(Nonconformance(inspection, HoldReleaseInPurpose, StatusTransferIn, holdLocation, Unrestricted,
                quantity, UnitCostFor(inspection.SkuCode), closedAtUtc));
            drafts.Add(Nonconformance(inspection, HoldReturnOutPurpose, Transfer, holdLocation, Unrestricted,
                -quantity, null, closedAtUtc));
            drafts.Add(Nonconformance(inspection, HoldReturnInPurpose, Transfer,
                WorldHistoryPhase2Spec.StorageLocationFor(inspection.SkuCode), Unrestricted,
                quantity, UnitCostFor(inspection.SkuCode), closedAtUtc));
        }
    }

    private static MovementDraft Nonconformance(
        WorldHistoryInspectionFact inspection,
        string purpose,
        string movementType,
        string locationCode,
        string qualityStatus,
        decimal quantity,
        decimal? unitCost,
        DateTimeOffset postedAtUtc) =>
        new(
            Purpose: purpose,
            MovementType: movementType,
            SourceDocumentId: inspection.NcrCode!,
            SourceDocumentLineId: inspection.SourceDocumentId,
            IdempotencyKey: WorldHistoryPhase2Spec.MovementKey(inspection.NcrCode!, purpose),
            SkuCode: inspection.SkuCode,
            UomCode: inspection.UomCode,
            LocationCode: locationCode,
            LotNo: inspection.BatchNo,
            QualityStatus: qualityStatus,
            Quantity: quantity,
            UnitCost: unitCost,
            PostedAtUtc: postedAtUtc);

    #endregion

    /// <summary>
    /// 累加耗用量，并锁死计量单位：同一物料一旦出现两种单位，台账维度就会分裂成两条，
    /// 「现存量 = 期初 + 入 − 出」的恒等式随之作废，因此这里直接失败而不是悄悄取第一个。
    /// </summary>
    private static WorldHistoryOpeningDemand Accumulate(
        Dictionary<string, WorldHistoryOpeningDemand> demand,
        string skuCode,
        string uomCode,
        decimal quantity)
    {
        if (!demand.TryGetValue(skuCode, out var current))
        {
            return new WorldHistoryOpeningDemand(uomCode, quantity);
        }

        if (!string.Equals(current.UomCode, uomCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"物料 {skuCode} 在历史领料里出现了两种计量单位（{current.UomCode} / {uomCode}），库存台账维度会分裂。");
        }

        return current with { Quantity = current.Quantity + quantity };
    }

    private static DateTimeOffset Later(DateTimeOffset candidate, DateTimeOffset floor) =>
        candidate > floor ? candidate : floor;

    private sealed record MovementDraft(
        string Purpose,
        string MovementType,
        string SourceDocumentId,
        string? SourceDocumentLineId,
        string IdempotencyKey,
        string SkuCode,
        string UomCode,
        string LocationCode,
        string? LotNo,
        string QualityStatus,
        decimal Quantity,
        decimal? UnitCost,
        DateTimeOffset PostedAtUtc);
}

/// <summary>某物料在本区间的累计耗用量（含单位），期初建账据此放大取整。</summary>
public sealed record WorldHistoryOpeningDemand(string UomCode, decimal Quantity);

/// <summary>一笔历史库存移动（含它落在哪条台账维度上）。</summary>
public sealed record WorldHistoryStockMovementFact(
    int Sequence,
    string Purpose,
    string MovementType,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string LocationCode,
    string? LotNo,
    string QualityStatus,
    decimal Quantity,
    decimal? UnitCost,
    DateTimeOffset PostedAtUtc)
{
    /// <summary>台账维度键：与 <c>StockLedger</c> 的唯一索引同构（历史不使用序列号与效期维度）。</summary>
    public string DimensionKey => string.Create(
        CultureInfo.InvariantCulture,
        $"{SkuCode}|{UomCode}|{WorldHistorySpec.SiteCode}|{LocationCode}|{LotNo ?? "-"}|{QualityStatus}|{WorldHistoryInventorySpec.OwnerType}");

    /// <summary>幂等查重键：与 <c>stock_movements</c> 的唯一索引（源服务 + 源单据 + 幂等键）同构。</summary>
    public string MovementKey => string.Create(
        CultureInfo.InvariantCulture,
        $"{SourceDocumentId}|{IdempotencyKey}");
}
