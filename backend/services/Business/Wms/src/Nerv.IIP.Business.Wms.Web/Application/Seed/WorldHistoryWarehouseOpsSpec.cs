using System.Globalization;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **仓储自动化与来料退货**侧的确定性规格。
///
/// 覆盖三张此前恒为 0 行、导致「WCS 任务」「盘点执行」「入库 · 退货」三页全空的表：
/// <list type="bullet">
/// <item><c>wcs_tasks</c> / <c>wcs_dispatch_circuits</c>：设备下发任务与每设备的下发熔断状态；</item>
/// <item><c>supplier_return_requests</c>：来料复检不合格的退供申请；</item>
/// <item><c>count_executions</c>：循环盘点执行（计划来自共享的 <see cref="WorldHistoryCountSpec"/>）。</item>
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
