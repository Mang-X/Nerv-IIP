using System.Globalization;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **四期（库存域）**：库存预留（<c>stock_reservations</c>）的
/// 确定性纯函数 Spec。
///
/// <para><b>要修的缺陷</b>：L1 库存引擎只写流水，从不走 <c>ReserveStock</c>，于是每条台账的
/// <c>ReservedQuantity</c> 恒为 0、<c>AvailableQuantity == OnHandQuantity</c>，
/// 「库存可用量」页的「已占用 / 可用」两列在整个演示里是死列。本块补上预留链路的历史。</para>
///
/// <para><b>恒等式红线（本块最重要的约束）</b>：库存一致性校验器按
/// 「现存量 = 世界观流水代数和（只认 <c>seed:world-history</c>）」重算。因此预留
/// <b>只能动 <c>ReservedQuantity</c> 与 <c>LedgerVersion</c></b>：
/// <list type="number">
/// <item>绝不改 <c>OnHandQuantity</c>——领域侧本就如此（<c>StockLedger.Reserve</c> 只加
///       <c>ReservedQuantity</c>），本块也绝不额外改写；</item>
/// <item>绝不写新的库存流水——预留不是移动，一笔都不落 <c>stock_movements</c>；</item>
/// <item>预留量恒不超过可用量（<c>AvailableQuantity</c>），否则 <c>Reserve</c> 直接拒绝。</item>
/// </list>
/// 三条都在 <see cref="WorldHistoryConsistencyValidator"/> 里 fail-closed 复核。</para>
///
/// <para><b>两个家族</b>（都不自造事实，逐条派生自已有的确定性形状）：</para>
/// <list type="number">
/// <item><b>发货拣货预留（已释放）</b>——每张发货出库单 <c>OB-DO-2026-#####</c> 在拣货时对产出批
///       <c>LOT-{工单}</c> 下预留，发货过账时释放。维度、批号、数量、释放时刻全部直接取自
///       <see cref="WorldHistoryInventorySpec.BuildMovements"/> 里那笔 <c>delivery-out</c> 流水，
///       因此**按构造**落在真实存在的台账维度上，也**按构造**与发货量逐件相等。
///       净效应为零（占用后即释放），演示里它是「发货时释放」的历史证据。</item>
/// <item><b>齐套待领预留（未释放）</b>——已下达待开工的工单（<c>ReleasedOnly</c>，设定集 §7 的
///       「已下达待开工 3%」）尚未领料，其四项主料在常驻库位的期初批 <c>LOT-OPENING-{物料}</c>
///       上仍被占着。这一家族**保持 open**，是「已占用 / 可用」两列在演示当天有值的唯一来源。</item>
/// </list>
///
/// <para><b>裁决点 · 为什么没有「未发货的出库单」可挂</b>：WMS 侧的世界观出库单
/// （<c>WorldHistoryWmsSpec</c> → <c>WriteOutboundChain</c>）**一律被推到 Completed**，
/// 没有留任何未发货的开放出库单；而所有走到完工入库的工单（<c>Closed</c>）其销售订单必然
/// 已发货（<c>Settled</c> / <c>Shipped</c>），成品批发完即归零。因此「未发货出库单的预留」
/// 在现有世界观里没有承载体。把它硬造出来要么改 WMS 的黄金向量，要么在成品库凭空建账——
/// 两者都比「按已下达工单的齐套需求预留原料」更假。齐套预留是同一条业务链的上游一跳
/// （下达 → 齐套预留 → 拣货 → 领料出库），既真实又有物理库存可占。</para>
///
/// <para><b>裁决点 · 已释放的历史预留不回放到台账</b>：家族一的净效应是
/// <c>+q</c> 再 <c>-q</c>，对 <c>ReservedQuantity</c> 恒为零。历史发货早已完成，
/// 成品批台账此刻是 0，回放 <c>Reserve</c> 会被「预留超过可用量」当场拒绝——
/// 那是对**今天**的库存状态做的检查，用它去复核**当时**成立的占用毫无意义。
/// 因此家族一只落预留台账行（<c>Reserve</c> 工厂 + <c>Release</c>，状态收敛到 released），
/// 不碰 <c>StockLedger</c>；家族二才真正调 <c>ledger.Reserve</c>。</para>
///
/// <para><b>裁决点 · 未释放预留的失效时刻必须在未来</b>：<c>ExpiredStockReservationService</c>
/// 会把 <c>ExpiresAtUtc</c> 已过期的 open 预留自动释放。若把它回填成历史时刻，
/// 演示环境启动没多久「已占用」列就又归零了。因此 open 预留的失效时刻取
/// 截止日 + <see cref="OpenReservationExpiryDays"/> 天。</para>
/// </summary>
public static class WorldHistoryReservationSpec
{
    /// <summary>预留的源服务名（与流水同一标记，隔离于租户真实预留与固定演示事实）。</summary>
    public const string SourceService = WorldHistoryInventorySpec.SourceService;

    /// <summary>未释放预留的失效期：截止日 + 180 天，保证演示期内不被过期扫描自动释放。</summary>
    public const int OpenReservationExpiryDays = 180;

    /// <summary>拣货预留早于发货过账的提前量（分钟）。完工入库至少早于发货 60 分钟，取 45 不会跑到入库之前。</summary>
    public const int PickReserveLeadMinutes = 45;

    #region 号段（本块不新增单号，全部复用既有段）

    /// <summary>发货拣货预留的幂等键：一张出库单一条预留。</summary>
    public static string PickReservationKey(string outboundOrderNo) => $"{outboundOrderNo}:pick-reserve";

    /// <summary>齐套待领预留的幂等键：一张工单每项主料一条预留。</summary>
    public static string KitReservationKey(string workOrderNo, string skuCode) => $"{workOrderNo}:kit-reserve:{skuCode}";

    /// <summary>本块引用的源单据号前缀，供隔离性回归断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["OB-DO-2026-", "WO-2026-"];

    #endregion

    /// <summary>
    /// 全量预留计划。seed 与校验器共用它，于是「写入的东西」与「校验的东西」不可能漂移。
    ///
    /// 两个家族都从 <see cref="WorldHistoryInventorySpec.BuildMovements"/> 的结果里取维度，
    /// 不自行推导批号/库位/单位——推两遍就有漂移两遍的机会，而台账维度对不上就是空页面。
    /// </summary>
    public static IReadOnlyList<WorldHistoryReservationPlan> BuildReservations(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var movements = WorldHistoryInventorySpec.BuildMovements(asOfDate, scale);
        var plans = new List<WorldHistoryReservationPlan>(1024);
        AppendPickReservations(movements, plans);
        AppendKitReservations(asOfDate, scale, movements, plans);

        return
        [
            .. plans
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.IdempotencyKey, StringComparer.Ordinal),
        ];
    }

    #region 一 · 发货拣货预留（发货时释放）

    private static void AppendPickReservations(
        IReadOnlyList<WorldHistoryStockMovementFact> movements,
        List<WorldHistoryReservationPlan> plans)
    {
        var goLiveUtc = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate, TimeOnly.MinValue, TimeSpan.Zero);
        foreach (var delivery in movements.Where(x =>
            string.Equals(x.Purpose, WorldHistoryInventorySpec.DeliveryOutPurpose, StringComparison.Ordinal)))
        {
            var outboundOrderNo = WorldHistoryPhase2Spec.OutboundOrderNo(delivery.SourceDocumentId);
            var releasedAtUtc = delivery.PostedAtUtc;
            var createdAtUtc = releasedAtUtc.AddMinutes(-PickReserveLeadMinutes);
            if (createdAtUtc < goLiveUtc)
            {
                createdAtUtc = goLiveUtc;
            }

            plans.Add(new WorldHistoryReservationPlan(
                Kind: WorldHistoryReservationKind.DeliveryPick,
                SourceDocumentId: outboundOrderNo,
                SourceDocumentLineId: "10",
                IdempotencyKey: PickReservationKey(outboundOrderNo),
                SkuCode: delivery.SkuCode,
                UomCode: delivery.UomCode,
                LocationCode: delivery.LocationCode,
                LotNo: delivery.LotNo,
                QualityStatus: delivery.QualityStatus,
                // 发货流水是负数，预留量取其绝对值：预留与实发逐件相等。
                Quantity: -delivery.Quantity,
                CreatedAtUtc: createdAtUtc,
                ReleasedAtUtc: releasedAtUtc));
        }
    }

    #endregion

    #region 二 · 齐套待领预留（保持 open）

    private static void AppendKitReservations(
        DateOnly asOfDate,
        double scale,
        IReadOnlyList<WorldHistoryStockMovementFact> movements,
        List<WorldHistoryReservationPlan> plans)
    {
        // 期初批的维度只认库里真实建过账的那一条：物料在本区间没有任何耗用时期初批根本不存在，
        // 硬挂上去就是一条指向空台账的预留（页面一点开就是空）。
        var openingBySku = movements
            .Where(x => string.Equals(x.Purpose, WorldHistoryInventorySpec.OpeningPurpose, StringComparison.Ordinal))
            .ToDictionary(x => x.SkuCode, StringComparer.Ordinal);

        foreach (var fact in WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale)
                     .Where(x => x.Execution == WorldHistoryExecutionDepth.ReleasedOnly))
        {
            var workOrderNo = fact.Plan.WorkOrderNo;
            var releaseDay = WorldHistoryQualitySpec.ClampToHistory(fact.Timeline.WorkOrderReleaseDate, asOfDate);
            var reservedAtUtc = WorldHistoryPhase2Spec.MomentOn(releaseDay, workOrderNo, "stock-kit-reserve");

            foreach (var component in WorldHistoryMesSpec.Components(fact.Plan.SkuCode))
            {
                if (!openingBySku.TryGetValue(component.SkuCode, out var opening))
                {
                    continue;
                }

                plans.Add(new WorldHistoryReservationPlan(
                    Kind: WorldHistoryReservationKind.WorkOrderKit,
                    SourceDocumentId: workOrderNo,
                    SourceDocumentLineId: component.SkuCode,
                    IdempotencyKey: KitReservationKey(workOrderNo, component.SkuCode),
                    SkuCode: opening.SkuCode,
                    UomCode: opening.UomCode,
                    LocationCode: opening.LocationCode,
                    LotNo: opening.LotNo,
                    QualityStatus: opening.QualityStatus,
                    Quantity: component.QuantityPer * fact.Plan.WorkOrderQuantity,
                    CreatedAtUtc: reservedAtUtc,
                    ReleasedAtUtc: null));
            }
        }
    }

    #endregion

    /// <summary>未释放预留的失效时刻：截止日 + 180 天（绝不落在过去，见类注释裁决点）。</summary>
    public static DateTimeOffset OpenReservationExpiresAtUtc(DateOnly asOfDate) =>
        new(asOfDate.AddDays(OpenReservationExpiryDays), TimeOnly.MinValue, TimeSpan.Zero);
}

/// <summary>预留家族。</summary>
public enum WorldHistoryReservationKind
{
    /// <summary>发货出库单的拣货预留——发货过账时释放（历史里一律 released）。</summary>
    DeliveryPick,

    /// <summary>已下达待开工工单的齐套预留——尚未领料，保持 open。</summary>
    WorkOrderKit,
}

/// <summary>一条历史库存预留（含它落在哪条台账维度上）。</summary>
public sealed record WorldHistoryReservationPlan(
    WorldHistoryReservationKind Kind,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string LocationCode,
    string? LotNo,
    string QualityStatus,
    decimal Quantity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReleasedAtUtc)
{
    /// <summary>台账维度键：与 <see cref="WorldHistoryStockMovementFact.DimensionKey"/> 同构。</summary>
    public string DimensionKey => string.Create(
        CultureInfo.InvariantCulture,
        $"{SkuCode}|{UomCode}|{WorldHistorySpec.SiteCode}|{LocationCode}|{LotNo ?? "-"}|{QualityStatus}|{WorldHistoryInventorySpec.OwnerType}");

    /// <summary>幂等查重键：与 <c>stock_reservations</c> 的唯一索引（源服务 + 源单据 + 幂等键）同构。</summary>
    public string ReservationKey => string.Create(
        CultureInfo.InvariantCulture,
        $"{SourceDocumentId}|{IdempotencyKey}");

    /// <summary>是否仍占着库存（决定它是否抬高台账的 <c>ReservedQuantity</c>）。</summary>
    public bool IsOpen => ReleasedAtUtc is null;

    /// <summary>期望的预留状态（与 <c>StockReservation</c> 的状态字面量一致）。</summary>
    public string ExpectedStatus => IsOpen ? "open" : "released";
}
