namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **二期仓储域**的确定性单据流。
///
/// 四类作业单全部**派生自已有的共享形状**，仓储域不自造任何业务事实，且与二期库存域的移动流水一一对应：
/// <list type="number">
/// <item>收货入库单 <c>IB-PR-2026-####</c>：一期 ERP 的采购收货 → 暂存待检 → 来料放行 → 上架任务；</item>
/// <item>完工入库单 <c>IB-FGR-WO-2026-#####</c>：一期 MES 的完工入库请求 → 成品库上架任务；</item>
/// <item>发货出库单 <c>OB-DO-2026-#####</c>：一期 ERP 的发货单 → 成品库拣货 → 复核 → 过账完成；</item>
/// <item>领料出库单 <c>OB-MIR-{工单}-{序号}</c>：一期 MES 的领料单 → 常驻库位拣货 → 线边库。</item>
/// </list>
///
/// 本类型是纯函数：同一 <c>(asOfDate, scale)</c> 必得同一张单据表，seed 与校验器共用它。
///
/// <para>
/// 裁决点一 · **时刻公式与库存域逐字复制**。仓储单据与库存流水必须「同号同时刻」，
/// 而两个服务不通信，只能靠两侧用同一套公式从同一份共享形状推出同一个时刻。
/// 因此本文件里的 <c>stock-receipt</c> / <c>stock-qc-release</c> / <c>stock-issue</c> /
/// <c>stock-fg-receipt</c> / <c>stock-delivery</c> 五个流键与偏移量，与库存域
/// <c>WorldHistoryInventorySpec</c> 逐字面量一致，改动任何一侧都必须同步改另一侧。
/// </para>
/// <para>
/// 裁决点二 · **批次维度取库存域的口径**。领料行的批次用期初批 <c>LOT-OPENING-{物料}</c>
/// 而不是 MES 的车间投料批 <c>LOT-{组件}-{工单}</c>：出库行会推出
/// <c>InventoryMovementRequest</c>，批次必须与库存台账维度对得上，否则请求过账必失败。
/// </para>
/// <para>
/// 裁决点三 · **来料放行统一记 passed**。质量域的来料 NCR 只会判返工或让步，
/// 两者在 WMS 收货门禁上都是「放行上架」（<c>IsReleasedForPutaway</c>），
/// 因此这里统一用 <c>quality.InspectionPassed</c>，无需把质量域规格再复制一份到仓储侧。
/// </para>
/// <para>
/// 裁决点四 · **执行人无处可落**。<c>InboundOrder</c> / <c>OutboundOrder</c> / <c>WarehouseTask</c>
/// 三个聚合都没有作业人字段（领域层从未建模执行人），因此库管的分配结果只出现在
/// <see cref="WorldHistoryInboundDocument.ExecutorUserId"/> /
/// <see cref="WorldHistoryOutboundDocument.ExecutorUserId"/> 与校验器抽样里，落不进库。
/// </para>
/// </summary>
public static class WorldHistoryWmsSpec
{
    public const string OwnerType = "company";
    public const string LineNo = "10";

    /// <summary>暂存 / 拣货行的质量状态（与 Inventory <c>StockQualityStatus</c> 同字面量）。</summary>
    public const string QualityInspection = "quality";
    public const string Unrestricted = "unrestricted";

    #region 源单据类型

    public const string PurchaseReceiptSourceType = "purchase-receipt";
    public const string ProductionReceiptSourceType = "production-receipt";
    public const string DeliveryOrderSourceType = "delivery-order";
    public const string MaterialIssueSourceType = "material-issue";

    #endregion

    #region 号段

    /// <summary>复核单号。</summary>
    public static string PackReviewNo(string outboundOrderNo) => $"PKR-{outboundOrderNo}";

    /// <summary>放行凭据代理号：跨库拿不到质量检验记录的 GUID，用确定性代理号记录放行来源。</summary>
    public static string InspectionRecordReference(string sourceDocumentId) => $"IR-{sourceDocumentId}";

    /// <summary>期初批次号：与库存域 <c>WorldHistoryInventorySpec.OpeningLotNo</c> 同字面量。</summary>
    public static string OpeningLotNo(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        return $"LOT-OPENING-{skuCode}";
    }

    /// <summary>本引擎产出的全部仓储号段前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["IB-", "OB-", "WT-", "PKR-", "IR-", "LOT-"];

    #endregion

    #region 移动幂等键的用途段（与库存域同字面量：两侧靠它对上同一笔流水）

    public const string ReceiptInPurpose = "receipt-in";
    public const string FinishedGoodsInPurpose = "fg-receipt";
    public const string DeliveryOutPurpose = "delivery-out";
    public const string MaterialIssueOutPurpose = "issue-out";

    #endregion

    /// <summary>全量仓储作业单据。入库与出库在同一次遍历里生成，以便发货单能引用完工入库的时刻。</summary>
    public static WorldHistoryWarehouseDocuments BuildDocuments(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var inbounds = new List<WorldHistoryInboundDocument>(512);
        var outbounds = new List<WorldHistoryOutboundDocument>(2048);

        BuildPurchaseInbounds(asOfDate, scale, inbounds);
        var finishedGoodsMoments = BuildProductionDocuments(asOfDate, scale, inbounds, outbounds);
        BuildDeliveryOutbounds(asOfDate, scale, outbounds, finishedGoodsMoments);

        return new WorldHistoryWarehouseDocuments(inbounds, outbounds);
    }

    #region 收货入库单

    private static void BuildPurchaseInbounds(DateOnly asOfDate, double scale, List<WorldHistoryInboundDocument> inbounds)
    {
        foreach (var purchase in WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale)
                     .Where(plan => plan.IsReceived))
        {
            var receiptNo = purchase.PurchaseReceiptNo;
            var inboundOrderNo = WorldHistoryPhase2Spec.InboundOrderNo(receiptNo);
            var receiptDay = ClampToHistory(purchase.ReceiptDate, asOfDate);
            var releaseDay = ClampToHistory(WorldHistoryCalendar.AddWorkingDays(receiptDay, 1), asOfDate);

            var receivedAtUtc = WorldHistoryPhase2Spec.MomentOn(receiptDay, receiptNo, "stock-receipt");
            var releasedAtUtc = Later(
                WorldHistoryPhase2Spec.MomentOn(releaseDay, receiptNo, "stock-qc-release"),
                receivedAtUtc.AddMinutes(60));
            var putawayAtUtc = releasedAtUtc.AddMinutes(45);

            inbounds.Add(new WorldHistoryInboundDocument(
                InboundOrderNo: inboundOrderNo,
                SourceDocumentType: PurchaseReceiptSourceType,
                SourceDocumentId: receiptNo,
                SkuCode: purchase.SkuCode,
                UomCode: purchase.UomCode,
                Quantity: purchase.Quantity,
                StagingLocationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                PutawayFromLocationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                PutawayToLocationCode: WorldHistoryPhase2Spec.StorageLocationFor(purchase.SkuCode),
                LotNo: WorldHistoryProcurementSpec.PurchasedLotNo(purchase.PurchaseOrderNo),
                QualityStatus: QualityInspection,
                WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(inboundOrderNo, 1),
                MovementIdempotencyKey: WorldHistoryPhase2Spec.MovementKey(receiptNo, ReceiptInPurpose),
                InspectionRecordId: InspectionRecordReference(receiptNo),
                ExecutorUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.Storekeepers, inboundOrderNo).UserId,
                CreatedAtUtc: receivedAtUtc,
                CompletedAtUtc: receivedAtUtc,
                QualityReleasedAtUtc: releasedAtUtc,
                TaskCreatedAtUtc: releasedAtUtc,
                TaskCompletedAtUtc: putawayAtUtc));
        }
    }

    #endregion

    #region 完工入库单 + 领料出库单

    /// <summary>写生产侧单据，并返回「工单号 → 完工入库时刻」供发货单保证时序单调。</summary>
    private static Dictionary<string, DateTimeOffset> BuildProductionDocuments(
        DateOnly asOfDate,
        double scale,
        List<WorldHistoryInboundDocument> inbounds,
        List<WorldHistoryOutboundDocument> outbounds)
    {
        var finishedGoodsMoments = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var fact in WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale))
        {
            var lastIssueMoment = DateTimeOffset.MinValue;
            foreach (var issue in WorldHistoryPhase2Spec.MaterialIssues(fact))
            {
                var outboundOrderNo = WorldHistoryPhase2Spec.OutboundOrderNo(issue.RequestNo);
                var issuedAtUtc = WorldHistoryPhase2Spec.MomentOn(
                    ClampToHistory(issue.IssueDate, asOfDate), issue.RequestNo, "stock-issue");
                lastIssueMoment = Later(issuedAtUtc, lastIssueMoment);

                outbounds.Add(new WorldHistoryOutboundDocument(
                    OutboundOrderNo: outboundOrderNo,
                    SourceDocumentType: MaterialIssueSourceType,
                    SourceDocumentId: issue.RequestNo,
                    SkuCode: issue.SkuCode,
                    UomCode: issue.UomCode,
                    Quantity: issue.Quantity,
                    PickFromLocationCode: WorldHistoryPhase2Spec.StorageLocationFor(issue.SkuCode),
                    PickToLocationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
                    LotNo: OpeningLotNo(issue.SkuCode),
                    WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(outboundOrderNo, 1),
                    PackReviewNo: PackReviewNo(outboundOrderNo),
                    MovementIdempotencyKey: WorldHistoryPhase2Spec.MovementKey(issue.RequestNo, MaterialIssueOutPurpose),
                    ExecutorUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.Storekeepers, outboundOrderNo).UserId,
                    CreatedAtUtc: issuedAtUtc,
                    TaskCompletedAtUtc: issuedAtUtc,
                    CompletedAtUtc: issuedAtUtc));
            }

            if (!fact.HasFinishedGoodsReceipt)
            {
                continue;
            }

            var receiptNo = fact.FinishedGoodsReceiptNo;
            var inboundOrderNo = WorldHistoryPhase2Spec.InboundOrderNo(receiptNo);
            var completionDay = ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate);
            var finishedGoodsAtUtc = Later(
                WorldHistoryPhase2Spec.MomentOn(completionDay, receiptNo, "stock-fg-receipt"),
                lastIssueMoment.AddMinutes(45));
            finishedGoodsMoments[fact.Plan.WorkOrderNo] = finishedGoodsAtUtc;

            inbounds.Add(new WorldHistoryInboundDocument(
                InboundOrderNo: inboundOrderNo,
                SourceDocumentType: ProductionReceiptSourceType,
                SourceDocumentId: receiptNo,
                SkuCode: fact.Plan.SkuCode,
                UomCode: WorldHistorySpec.UomCode,
                Quantity: fact.Plan.GoodQuantity,
                // 成品完工后账面一次落在成品库（与 MES 的 INV-{工单} 同库位）；
                // 上架任务描述的是「车间线边下线 → 成品库」这段物理搬运。
                StagingLocationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                PutawayFromLocationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
                PutawayToLocationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                LotNo: fact.ProducedLotNo,
                QualityStatus: Unrestricted,
                WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(inboundOrderNo, 1),
                MovementIdempotencyKey: fact.FinishedGoodsMovementId,
                InspectionRecordId: null,
                ExecutorUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.Storekeepers, inboundOrderNo).UserId,
                CreatedAtUtc: finishedGoodsAtUtc,
                CompletedAtUtc: finishedGoodsAtUtc,
                QualityReleasedAtUtc: null,
                TaskCreatedAtUtc: finishedGoodsAtUtc,
                TaskCompletedAtUtc: finishedGoodsAtUtc.AddMinutes(30)));
        }

        return finishedGoodsMoments;
    }

    #endregion

    #region 发货出库单

    private static void BuildDeliveryOutbounds(
        DateOnly asOfDate,
        double scale,
        List<WorldHistoryOutboundDocument> outbounds,
        Dictionary<string, DateTimeOffset> finishedGoodsMoments)
    {
        foreach (var order in WorldHistorySpec.BuildOrderPlans(asOfDate, scale).Where(plan => plan.HasDelivery))
        {
            var deliveryOrderNo = WorldHistorySpec.DeliveryOrderNo(order.Index);
            var outboundOrderNo = WorldHistoryPhase2Spec.OutboundOrderNo(deliveryOrderNo);
            var shipDay = ClampToHistory(WorldHistoryTimeline.For(order, asOfDate).ShipDate, asOfDate);
            var shippedAtUtc = WorldHistoryPhase2Spec.MomentOn(shipDay, deliveryOrderNo, "stock-delivery");
            if (finishedGoodsMoments.TryGetValue(order.WorkOrderNo, out var finishedGoodsAtUtc))
            {
                shippedAtUtc = Later(shippedAtUtc, finishedGoodsAtUtc.AddMinutes(60));
            }

            outbounds.Add(new WorldHistoryOutboundDocument(
                OutboundOrderNo: outboundOrderNo,
                SourceDocumentType: DeliveryOrderSourceType,
                SourceDocumentId: deliveryOrderNo,
                SkuCode: order.SkuCode,
                UomCode: WorldHistorySpec.UomCode,
                Quantity: order.Quantity,
                PickFromLocationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                PickToLocationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                LotNo: WorldHistoryMesSpec.ProducedLotNo(order.WorkOrderNo),
                WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(outboundOrderNo, 1),
                PackReviewNo: PackReviewNo(outboundOrderNo),
                MovementIdempotencyKey: WorldHistoryPhase2Spec.MovementKey(deliveryOrderNo, DeliveryOutPurpose),
                ExecutorUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.Storekeepers, outboundOrderNo).UserId,
                CreatedAtUtc: shippedAtUtc,
                TaskCompletedAtUtc: shippedAtUtc.AddMinutes(20),
                CompletedAtUtc: shippedAtUtc.AddMinutes(40)));
        }
    }

    #endregion

    /// <summary>把候选日期夹进 <c>[上线日, asOfDate]</c> 并回退到工作日（与质量 / 库存两侧同字面量）。</summary>
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
}

/// <summary>一次生成得到的全部仓储作业单据。</summary>
public sealed record WorldHistoryWarehouseDocuments(
    IReadOnlyList<WorldHistoryInboundDocument> InboundOrders,
    IReadOnlyList<WorldHistoryOutboundDocument> OutboundOrders);

/// <summary>一张历史入库单（收货入库或完工入库）及其上架任务。</summary>
public sealed record WorldHistoryInboundDocument(
    string InboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string StagingLocationCode,
    string PutawayFromLocationCode,
    string PutawayToLocationCode,
    string? LotNo,
    string QualityStatus,
    string WarehouseTaskNo,
    string MovementIdempotencyKey,
    string? InspectionRecordId,
    string ExecutorUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset? QualityReleasedAtUtc,
    DateTimeOffset TaskCreatedAtUtc,
    DateTimeOffset TaskCompletedAtUtc)
{
    /// <summary>收货入库要过来料检验门禁；完工入库已在 MES 终检合格，直接上架。</summary>
    public bool RequiresQualityInspection =>
        string.Equals(QualityStatus, WorldHistoryWmsSpec.QualityInspection, StringComparison.Ordinal);
}

/// <summary>一张历史出库单（发货出库或领料出库）及其拣货任务与复核。</summary>
public sealed record WorldHistoryOutboundDocument(
    string OutboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string PickFromLocationCode,
    string PickToLocationCode,
    string? LotNo,
    string WarehouseTaskNo,
    string PackReviewNo,
    string MovementIdempotencyKey,
    string ExecutorUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset TaskCompletedAtUtc,
    DateTimeOffset CompletedAtUtc);
