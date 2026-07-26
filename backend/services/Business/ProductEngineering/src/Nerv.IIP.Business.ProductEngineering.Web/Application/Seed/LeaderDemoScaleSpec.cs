namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 领导演示「规模块」的 ProductEngineering 侧固定形状：每个 SKU 一条 4 道工序的有前后置工艺路线，
/// 分布在 MasterData 规模块的 4 个工作中心上（合计 24 台可排设备）。号段与固定演示事实完全隔离。
/// </summary>
public static class LeaderDemoScaleSpec
{
    public const string RawMaterialSkuCode = "SKU-SCALE-RM-001";

    public static readonly LeaderDemoScaleSku[] FinishedSkus =
    [
        new("SKU-SCALE-001", "MBOM-SCALE-001", "ROUTING-SCALE-001"),
        new("SKU-SCALE-002", "MBOM-SCALE-002", "ROUTING-SCALE-002"),
        new("SKU-SCALE-003", "MBOM-SCALE-003", "ROUTING-SCALE-003"),
        new("SKU-SCALE-004", "MBOM-SCALE-004", "ROUTING-SCALE-004"),
        new("SKU-SCALE-005", "MBOM-SCALE-005", "ROUTING-SCALE-005"),
        new("SKU-SCALE-006", "MBOM-SCALE-006", "ROUTING-SCALE-006"),
    ];

    /// <summary>
    /// 4 道有前后置关系的工序。RunMinutes 为单件工时；BusinessScheduling 的工序时长口径为
    /// ceil(RunMinutes × 数量) + TeardownMinutes，因此单件 1 分钟配合 20–60 件的订单数量
    /// 得到 20–60 分钟量级的工序，与 APS 基准 (MAN-581 / #1050) 的可排性形状一致。
    /// 规模块工序一律不要求质检，避免整批工序被 quality 门禁直接判为不可排。
    /// </summary>
    public static readonly LeaderDemoScaleStage[] Stages =
    [
        new(10, "WC-SCALE-WELD", "OP-SCALE-WELD", "筒体焊接", 10, 1, 5),
        new(20, "WC-SCALE-ROD", "OP-SCALE-ROD", "活塞杆装配", 8, 1, 4),
        new(30, "WC-SCALE-SEAL", "OP-SCALE-SEAL", "油封压装", 12, 1, 6),
        new(40, "WC-SCALE-TEST", "OP-SCALE-TEST", "阻尼性能检测", 6, 1, 3),
    ];
}

public sealed record LeaderDemoScaleSku(string SkuCode, string MbomCode, string RoutingCode);

public sealed record LeaderDemoScaleStage(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    string OperationName,
    int SetupMinutes,
    int RunMinutes,
    int TeardownMinutes);
