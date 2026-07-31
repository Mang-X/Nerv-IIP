using System.Globalization;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **仓储自动化与来料退货**侧的确定性规格。
///
/// 覆盖此前缺失的自动化、盘点、退货事实，以及库管现场作业池与当前可执行队列：
/// <list type="bullet">
/// <item><c>wcs_tasks</c> / <c>wcs_dispatch_circuits</c>：设备下发任务与每设备的下发熔断状态；</item>
/// <item><c>supplier_return_requests</c>：来料复检不合格的退供申请；</item>
/// <item><c>count_executions</c>：循环盘点执行（计划来自共享的 <see cref="WorldHistoryCountSpec"/>）。</item>
/// <item>WMS 作业池、emp049 资格，以及收货 / 上架 / 拣货 / 复核的受控当前队列。</item>
/// </list>
///
/// <para>
/// 裁决点一 · **WCS 任务必须绑库里真实存在的仓储作业任务**。<c>WcsTask.WarehouseTaskId</c>
/// 是 <c>WarehouseTask</c> 的强类型主键，跨表造号会让「WCS 任务」页点开就 404。
/// 因此下发对象由 seed 从已落库的 <c>warehouse_tasks</c> 里按任务号确定性挑选
/// （<see cref="IsDispatched"/>），外部任务号取 <c>WCS-{仓储任务号}</c>——
/// 不需要全局序号即可幂等，且与 <c>*-DEMO-*</c> / <c>*-SCALE-*</c> 天然隔离。
/// </para>
/// <para>
/// 裁决点二 · **熔断器最终一律闭合**。<c>wcs_dispatch_circuits</c> 一旦处于打开态，
/// 演示当场的任何 WCS 下发都会被历史数据挡住。因此历史失败痕迹保留
/// （<c>ConsecutiveFailureCount</c> / <c>LastFailureAtUtc</c> / <c>ResetAtUtc</c> 都有值），
/// 但每条链路收敛在闭合态：这既是真实运维形态（失败→复位），也不给演示埋雷。
/// </para>
/// <para>
/// 裁决点三 · **退货申请是单据，不驱动库存**。<c>SupplierReturnRequest</c> 聚合本身不产生
/// 库存流水，库存域的一致性校验器是按「现存量 = 世界观流水代数和」重算的；历史退货若
/// 凭空扣减库存，恒等式会当场失衡。因此退货挂在**已上架的真实收货单**上，语义是
/// 「上架后复检发现来料缺陷、已发起退供」，退货对应的实物移动留到演示当场走真实路径。
/// </para>
/// </summary>
public static class WorldHistoryWarehouseOpsSpec
{
    #region 现场作业池与演示人员

    /// <summary>库管吴桂芳在 IAM 中的稳定 principal id（当前队列的直派对象）。</summary>
    public const string DemoWarehousePrincipalId = "user-emp-049";

    /// <summary>仓储主管 EMP-048 的稳定 principal id（设定集 §5 仓储物流部首位）。</summary>
    public const string WarehouseSupervisorPrincipalId = "user-emp-048";

    /// <summary>演示用平台管理员（与 <c>IamSeedOptions.AdminUserId</c> 同字面量）。</summary>
    public const string DemoAdministratorPrincipalId = "user-admin";

    public const string ReceivingPoolCode = "POOL-WMS-RECEIVING";
    public const string ShippingPoolCode = "POOL-WMS-SHIPPING";
    public const string CountPoolCode = "POOL-WMS-COUNT";
    public const string CurrentInboundOrderPrefix = "IB-WQ-";
    public const string CurrentOutboundOrderPrefix = "OB-WQ-";
    public const string CurrentInboundTaskPrefix = "WT-IB-WQ-";
    public const string CurrentOutboundTaskPrefix = "WT-OB-WQ-";

    /// <summary>
    /// SITE-001 的三类现场作业池。它们属于 WMS 资格边界，不复用 MasterData 班组。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryWarehouseWorkPoolSpec> WorkPools =
    [
        new(ReceivingPoolCode, "收货与上架", WorldHistorySpec.SiteCode),
        new(ShippingPoolCode, "拣货与发运", WorldHistorySpec.SiteCode),
        new(CountPoolCode, "循环盘点", WorldHistorySpec.SiteCode),
    ];

    /// <summary>
    /// 三个作业池的成员：**账面上干过活的人，系统里就必须有资格干活**。
    ///
    /// 成员集合 = 历史单据的全部执行人（<see cref="WorldHistoryPhase2Spec.Storekeepers"/>，
    /// 即 <c>user-emp-049..052</c>，见 <c>WorldHistoryWmsSpec</c> / <c>WorldHistoryCountSpec</c>
    /// 的 <c>ExecutorUserId</c>）+ 仓储主管 + 演示用平台管理员。
    ///
    /// <para>
    /// 裁决点 · **不做 admin 旁路，改为把 admin 写成真成员**。
    /// <c>WarehouseWorkScopeAuthorizer</c> 的 self / work-pool 范围一律以作业池成员资格为准
    /// （site 范围另由 IAM 精确站点授权成立）。在授权器里给管理员开后门等于把资格判定变成两套规则；
    /// 演示世界里 admin 本来就该是仓库的在册作业员，因此把他写进池子，授权器保持单一口径。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> WorkPoolPrincipalIds =
    [
        .. new[] { DemoAdministratorPrincipalId, WarehouseSupervisorPrincipalId }
            .Concat(WorldHistoryPhase2Spec.Storekeepers.Select(person => person.UserId))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// 一部分开放任务直接派给 emp049，其余留在作业池供现场认领。
    /// 判定只依赖业务号，缩放和写入顺序不会改变同一资源的归属。
    /// </summary>
    public static bool IsDirectDemoAssignment(string resourceReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceReference);
        return new WorldHistoryRandom($"wms-direct-assignment:{resourceReference}")
            .Chance(0.35d);
    }

    /// <summary>
    /// 构造演示日现场尚未闭环的真实作业队列。
    /// 每类两条是验收下限：第一条可直派 emp049，第二条留在池内待领。
    /// 所有业务字段来自世界观 SKU / 库位 / 批次规则，时间只依赖 as-of 日期。
    /// </summary>
    public static WorldHistoryWarehouseCurrentQueueSpec BuildCurrentQueue(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var queueDate = WorldHistoryWmsSpec.ClampToHistory(
            asOfDate < WorldHistoryCalendar.GoLiveDate
                ? WorldHistoryCalendar.GoLiveDate
                : asOfDate,
            asOfDate < WorldHistoryCalendar.GoLiveDate
                ? WorldHistoryCalendar.GoLiveDate
                : asOfDate);
        var dateSegment = queueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        // 当前队列只用让给它的那段维度：未回单的盘点任务一律避开这几条，
        // 否则演示当天「先拣货、再确认盘点」会把盘点快照版本捅穿（#1374）。
        var inventoryDimensions = WorldHistoryCountSpec.CurrentQueueDimensions;

        var receiptOrders = inventoryDimensions
            .Take(2)
            .Select((dimension, index) =>
            {
                var sourceDocumentId = $"WQ-A-RECEIPT-PR-{dateSegment}-{index + 1:D2}";
                return new WorldHistoryCurrentInboundQueueDraft(
                    InboundOrderNo: WorldHistoryPhase2Spec.InboundOrderNo(sourceDocumentId),
                    SourceDocumentType: WorldHistoryWmsSpec.PurchaseReceiptSourceType,
                    SourceDocumentId: sourceDocumentId,
                    SkuCode: dimension.SkuCode,
                    UomCode: dimension.UomCode,
                    Quantity: QueueQuantity(sourceDocumentId),
                    StagingLocationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                    LotNo: $"LOT-{sourceDocumentId}",
                    QualityStatus: WorldHistoryWmsSpec.QualityInspection,
                    WarehouseTaskNo: null,
                    PutawayFromLocationCode: null,
                    PutawayToLocationCode: null,
                    CreatedAtUtc: QueueMoment(queueDate, sourceDocumentId));
            })
            .ToArray();

        var putawayOrders = WorldHistorySpec.FinishedGoodSkus
            .Take(2)
            .Select((skuCode, index) =>
            {
                var sourceDocumentId = $"WQ-B-PUTAWAY-FGR-{dateSegment}-{index + 1:D2}";
                var inboundOrderNo = WorldHistoryPhase2Spec.InboundOrderNo(sourceDocumentId);
                return new WorldHistoryCurrentInboundQueueDraft(
                    InboundOrderNo: inboundOrderNo,
                    SourceDocumentType: WorldHistoryWmsSpec.ProductionReceiptSourceType,
                    SourceDocumentId: sourceDocumentId,
                    SkuCode: skuCode,
                    UomCode: WorldHistorySpec.UomCode,
                    Quantity: QueueQuantity(sourceDocumentId),
                    StagingLocationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                    LotNo: $"LOT-{sourceDocumentId}",
                    QualityStatus: WorldHistoryWmsSpec.Unrestricted,
                    WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(inboundOrderNo, 1),
                    PutawayFromLocationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
                    PutawayToLocationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                    CreatedAtUtc: QueueMoment(queueDate, sourceDocumentId));
            })
            .ToArray();

        var reviewOrders = inventoryDimensions
            .Skip(2)
            .Take(2)
            .Select((dimension, index) =>
                BuildOutboundQueueDraft(
                    dimension,
                    $"WQ-A-REVIEW-MIR-{dateSegment}-{index + 1:D2}",
                    reviewReady: true,
                    queueDate))
            .ToArray();
        var pickingOrders = inventoryDimensions
            .Skip(4)
            .Take(2)
            .Select((dimension, index) =>
                BuildOutboundQueueDraft(
                    dimension,
                    $"WQ-B-PICK-MIR-{dateSegment}-{index + 1:D2}",
                    reviewReady: false,
                    queueDate))
            .ToArray();

        // #1374 · 发货出库单：演示「销售发货 → 拣货 → 复核 → 发运」这条最核心的仓储链路，
        // 此前四张当前队列出库单**全是领料**，一个可点的发货对象都没有。
        // 这些单挂在 ERP 已开未发运的发货单上（订单阶段 PendingShipment），
        // 拣的正是那几张单自己的完工入库批——成品在库、发货单已开、就差仓库动手。
        var deliveryOrders = BuildPendingShipmentDrafts(asOfDate, scale, queueDate);

        return new WorldHistoryWarehouseCurrentQueueSpec(
            receiptOrders,
            putawayOrders,
            [.. reviewOrders, .. pickingOrders, .. deliveryOrders]);
    }

    /// <summary>
    /// 待发运的发货出库单：与 ERP 侧 <c>PendingShipment</c> 阶段的订单一一对应。
    ///
    /// <para>
    /// 裁决点 · **单号仍走 <c>OB-WQ-</c> 段，源单据号才是真的 <c>DO-2026-#####</c>**。
    /// 历史校验器按 <c>OB-WQ-</c> 前缀整体豁免当前队列；若这里直接用 <c>OB-DO-2026-#####</c>，
    /// 单子会掉进历史通道，被「历史出库单必须已完成」当场判失败。
    /// 号段隔离与业务可追溯因此分工：单号负责隔离，<c>SourceDocumentId</c> 负责追溯。
    /// </para>
    /// </summary>
    private static IReadOnlyList<WorldHistoryCurrentOutboundQueueDraft> BuildPendingShipmentDrafts(
        DateOnly asOfDate,
        double scale,
        DateOnly queueDate)
    {
        var drafts = new List<WorldHistoryCurrentOutboundQueueDraft>(WorldHistorySpec.PendingShipmentOrderCount);
        var pendingOrders = WorldHistorySpec.BuildOrderPlans(asOfDate, scale)
            .Where(plan => plan.HasPendingShipment)
            .OrderBy(plan => plan.Index)
            .ToArray();

        for (var position = 0; position < pendingOrders.Length; position++)
        {
            var order = pendingOrders[position];
            var deliveryOrderNo = WorldHistorySpec.DeliveryOrderNo(order.Index);
            var outboundOrderNo = $"{CurrentOutboundOrderPrefix}C-SHIP-{deliveryOrderNo}";
            drafts.Add(new WorldHistoryCurrentOutboundQueueDraft(
                OutboundOrderNo: outboundOrderNo,
                SourceDocumentType: WorldHistoryWmsSpec.DeliveryOrderSourceType,
                SourceDocumentId: deliveryOrderNo,
                SkuCode: order.SkuCode,
                UomCode: WorldHistorySpec.UomCode,
                Quantity: order.Quantity,
                PickFromLocationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                PickToLocationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                LotNo: WorldHistoryMesSpec.ProducedLotNo(order.WorkOrderNo),
                WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(outboundOrderNo, 1),
                // 至少留一张待复核：拣完等复核与还没拣，是发货链上两个不同的可演示切面。
                ReviewReady: position == 0,
                CreatedAtUtc: QueueMoment(queueDate, deliveryOrderNo)));
        }

        return drafts;
    }

    public static bool IsCurrentQueueInboundOrder(string inboundOrderNo) =>
        inboundOrderNo.StartsWith(CurrentInboundOrderPrefix, StringComparison.Ordinal);

    public static bool IsCurrentQueueOutboundOrder(string outboundOrderNo) =>
        outboundOrderNo.StartsWith(CurrentOutboundOrderPrefix, StringComparison.Ordinal);

    public static bool IsCurrentQueueTask(string taskNo) =>
        taskNo.StartsWith(CurrentInboundTaskPrefix, StringComparison.Ordinal)
        || taskNo.StartsWith(CurrentOutboundTaskPrefix, StringComparison.Ordinal);

    private static WorldHistoryCurrentOutboundQueueDraft BuildOutboundQueueDraft(
        WorldHistoryCountDimension dimension,
        string sourceDocumentId,
        bool reviewReady,
        DateOnly queueDate)
    {
        var outboundOrderNo = WorldHistoryPhase2Spec.OutboundOrderNo(sourceDocumentId);
        return new WorldHistoryCurrentOutboundQueueDraft(
            OutboundOrderNo: outboundOrderNo,
            SourceDocumentType: WorldHistoryWmsSpec.MaterialIssueSourceType,
            SourceDocumentId: sourceDocumentId,
            SkuCode: dimension.SkuCode,
            UomCode: dimension.UomCode,
            Quantity: QueueQuantity(sourceDocumentId),
            PickFromLocationCode: dimension.LocationCode,
            PickToLocationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
            LotNo: dimension.LotNo,
            WarehouseTaskNo: WorldHistoryPhase2Spec.WarehouseTaskNo(outboundOrderNo, 1),
            ReviewReady: reviewReady,
            CreatedAtUtc: QueueMoment(queueDate, sourceDocumentId));
    }

    private static decimal QueueQuantity(string sourceDocumentId) =>
        new WorldHistoryRandom($"wms-current-queue-quantity:{sourceDocumentId}")
            .NextQuantity(20, 100, 10);

    private static DateTimeOffset QueueMoment(DateOnly queueDate, string sourceDocumentId) =>
        WorldHistoryPhase2Spec.MomentOn(
            queueDate,
            sourceDocumentId,
            "warehouse-current-queue");

    #endregion

    #region WCS 设备与下发

    /// <summary>下发熔断阈值：连续失败达到该次数即打开链路（与 WMS 运行期默认口径同量级）。</summary>
    public const int CircuitFailureThreshold = 5;

    /// <summary>历史里参与 WCS 下发的仓储作业任务比例。</summary>
    public const double DispatchRatio = 0.12d;

    /// <summary>熔断器只回放最近若干次失败——历史失败上千次时逐条回放既无意义也拖慢 seed。</summary>
    public const int CircuitReplayFailures = 3;

    /// <summary>三条自动化链路，覆盖成品库堆垛机 / 原料库输送线 / 车间配送 AGV。</summary>
    public static readonly IReadOnlyList<WorldHistoryWcsDevice> Devices =
    [
        new("srm", "SRM-FG-01", "成品库堆垛机 1 号"),
        new("conveyor", "CONV-RM-01", "原料库输送线 1 号"),
        new("agv", "AGV-LINE-01", "车间配送 AGV 1 号"),
    ];

    /// <summary>外部 WCS 任务号（设定集 §9 二期补登记段）。</summary>
    public static string ExternalTaskId(string warehouseTaskNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouseTaskNo);
        return $"WCS-{warehouseTaskNo}";
    }

    /// <summary>本规格产出的号段前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["WCS-WT-", "RTS-IB-", "IR-"];

    /// <summary>某张仓储作业任务是否走 WCS 下发（确定性，与遍历顺序、缩放比例无关）。</summary>
    public static bool IsDispatched(string warehouseTaskNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouseTaskNo);
        return new WorldHistoryRandom($"wcs-dispatch:{warehouseTaskNo}").Chance(DispatchRatio);
    }

    /// <summary>按搬运的起讫库位选链路：涉及成品库走堆垛机，涉及线边库走 AGV，其余走输送线。</summary>
    public static WorldHistoryWcsDevice DeviceFor(string fromLocationCode, string toLocationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromLocationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(toLocationCode);
        if (Involves(WorldHistoryPhase2Spec.FinishedGoodsLocationCode))
        {
            return Devices[0];
        }

        return Involves(WorldHistoryPhase2Spec.LineSideLocationCode) ? Devices[2] : Devices[1];

        bool Involves(string locationCode) =>
            string.Equals(fromLocationCode, locationCode, StringComparison.Ordinal) ||
            string.Equals(toLocationCode, locationCode, StringComparison.Ordinal);
    }

    /// <summary>下发结局分布：已完成 86% / 执行中 6% / 失败 5% / 已取消 3%。</summary>
    public static WorldHistoryWcsOutcome OutcomeFor(string warehouseTaskNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouseTaskNo);
        var roll = new WorldHistoryRandom($"wcs-outcome:{warehouseTaskNo}").NextInt(0, 100);
        return roll switch
        {
            < 86 => WorldHistoryWcsOutcome.Completed,
            < 92 => WorldHistoryWcsOutcome.Dispatched,
            < 97 => WorldHistoryWcsOutcome.Failed,
            _ => WorldHistoryWcsOutcome.Cancelled,
        };
    }

    /// <summary>失败诊断（全中文，页面直接展示）。</summary>
    public static readonly IReadOnlyList<WorldHistoryWcsFailure> Failures =
    [
        new("WCS-TIMEOUT", "下位机应答超时，任务未被设备接收"),
        new("WCS-BLOCKED", "目标货位已被占用，堆垛机拒绝入库"),
        new("WCS-EMPTY", "取货位无料，光电检测未触发"),
        new("WCS-COMM", "通讯链路中断，任务在设备侧丢失"),
    ];

    public static WorldHistoryWcsFailure FailureFor(string warehouseTaskNo) =>
        new WorldHistoryRandom($"wcs-failure:{warehouseTaskNo}").Pick(Failures);

    /// <summary>下发报文：只放设备真正需要的搬运指令，键名全英文以免 JSON 里出现转义噪声。</summary>
    public static string DispatchPayload(
        string warehouseTaskNo,
        string fromLocationCode,
        string toLocationCode,
        decimal quantity) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"taskNo\":\"{warehouseTaskNo}\",\"from\":\"{fromLocationCode}\",\"to\":\"{toLocationCode}\",\"quantity\":{quantity:0.######}}}");

    /// <summary>回执报文。</summary>
    public static string CompletionPayload(string warehouseTaskNo, decimal quantity) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"taskNo\":\"{warehouseTaskNo}\",\"result\":\"completed\",\"quantity\":{quantity:0.######}}}");

    #endregion

    #region 来料退货

    /// <summary>来料复检判退货的比例。</summary>
    public const double SupplierReturnRatio = 0.03d;

    /// <summary>退货凭据代理号：与放行凭据 <c>IR-{收货单}</c> 显式分段，避免两条结论共用一个记录号。</summary>
    public static string ReturnInspectionRecordReference(string purchaseReceiptNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purchaseReceiptNo);
        return $"IR-{purchaseReceiptNo}-RTS";
    }

    /// <summary>退货原因（全中文）。</summary>
    public static readonly IReadOnlyList<string> ReturnReasons =
    [
        "上架后复检发现外径超差，判退供应商",
        "来料表面锈蚀，防锈处理不符合技术协议",
        "随货质保书缺失且供应商无法补齐，整批退回",
        "硬度抽检不合格，超出图纸下限",
        "包装破损导致零件磕碰，判退供应商",
    ];

    /// <summary>某张收货单是否发起退供（确定性）。</summary>
    public static bool HasSupplierReturn(string purchaseReceiptNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purchaseReceiptNo);
        return new WorldHistoryRandom($"supplier-return:{purchaseReceiptNo}").Chance(SupplierReturnRatio);
    }

    /// <summary>退货数量：收货量的 2%–8%，最少 1（聚合硬要求正数）。</summary>
    public static decimal ReturnQuantity(string purchaseReceiptNo, decimal receivedQuantity)
    {
        var random = new WorldHistoryRandom($"supplier-return-quantity:{purchaseReceiptNo}");
        var percent = random.NextInt(2, 9);
        return Math.Max(1m, decimal.Round(receivedQuantity * percent / 100m, 0, MidpointRounding.AwayFromZero));
    }

    public static string ReturnReason(string purchaseReceiptNo) =>
        new WorldHistoryRandom($"supplier-return-reason:{purchaseReceiptNo}").Pick(ReturnReasons);

    #endregion
}

public sealed record WorldHistoryWarehouseWorkPoolSpec(
    string PoolCode,
    string DisplayName,
    string SiteCode);

public sealed record WorldHistoryWarehouseCurrentQueueSpec(
    IReadOnlyList<WorldHistoryCurrentInboundQueueDraft> ReceiptOrders,
    IReadOnlyList<WorldHistoryCurrentInboundQueueDraft> PutawayOrders,
    IReadOnlyList<WorldHistoryCurrentOutboundQueueDraft> OutboundOrders);

public sealed record WorldHistoryCurrentInboundQueueDraft(
    string InboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string StagingLocationCode,
    string LotNo,
    string QualityStatus,
    string? WarehouseTaskNo,
    string? PutawayFromLocationCode,
    string? PutawayToLocationCode,
    DateTimeOffset CreatedAtUtc);

public sealed record WorldHistoryCurrentOutboundQueueDraft(
    string OutboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string PickFromLocationCode,
    string PickToLocationCode,
    string LotNo,
    string WarehouseTaskNo,
    bool ReviewReady,
    DateTimeOffset CreatedAtUtc);

/// <summary>一条 WCS 自动化链路。</summary>
public sealed record WorldHistoryWcsDevice(string AdapterType, string DeviceId, string DisplayName);

/// <summary>一条 WCS 失败诊断。</summary>
public sealed record WorldHistoryWcsFailure(string Code, string Message);

/// <summary>一次 WCS 下发的结局。</summary>
public enum WorldHistoryWcsOutcome
{
    /// <summary>设备已回执完成。</summary>
    Completed,

    /// <summary>已下发、设备执行中（页面的「进行中」）。</summary>
    Dispatched,

    /// <summary>设备回报失败（页面的「异常」）。</summary>
    Failed,

    /// <summary>上游撤单，下发被取消。</summary>
    Cancelled,
}
