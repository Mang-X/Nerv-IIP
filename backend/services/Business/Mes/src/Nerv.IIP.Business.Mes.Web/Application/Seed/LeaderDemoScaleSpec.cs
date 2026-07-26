namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 领导演示「规模块」的 MES 侧固定形状。订单派生公式与 ERP 侧 <c>LeaderDemoScaleSpec</c> 字面量一致，
/// 因此第 i 张 <c>WO-SCALE-#####</c> 与第 i 张 <c>SO-SCALE-#####</c> 一一对应；
/// 4 道工序与 ProductEngineering 规模路线一致，构成真实的前后置工序链。
/// </summary>
public static class LeaderDemoScaleSpec
{
    public static readonly string[] FinishedSkuCodes =
    [
        "SKU-SCALE-001",
        "SKU-SCALE-002",
        "SKU-SCALE-003",
        "SKU-SCALE-004",
        "SKU-SCALE-005",
        "SKU-SCALE-006",
    ];

    public static readonly LeaderDemoScaleStage[] Stages =
    [
        new(10, "WC-SCALE-WELD", "OP-SCALE-WELD", RunMinutes: 1, TeardownMinutes: 5),
        new(20, "WC-SCALE-ROD", "OP-SCALE-ROD", RunMinutes: 1, TeardownMinutes: 4),
        new(30, "WC-SCALE-SEAL", "OP-SCALE-SEAL", RunMinutes: 1, TeardownMinutes: 6),
        new(40, "WC-SCALE-TEST", "OP-SCALE-TEST", RunMinutes: 1, TeardownMinutes: 3),
    ];

    public static string WorkOrderId(int index) => $"WO-SCALE-{index:D5}";

    public static string OperationTaskId(int index, LeaderDemoScaleStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return $"{WorkOrderId(index)}-OP-{stage.Sequence}";
    }

    public static string SkuCode(int index) => FinishedSkuCodes[(index - 1) % FinishedSkuCodes.Length];

    public static decimal Quantity(int index) => 20m + ((index - 1) % 5) * 10m;

    public static int DueDayOffset(int index) => 14 + ((index - 1) % 29);

    public static bool IsRush(int index) => index % 29 == 0;

    public static int Priority(int index) => IsRush(index) ? 100 : 1 + (index % 9);

    public static TimeSpan Duration(int index, LeaderDemoScaleStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return TimeSpan.FromMinutes((double)Quantity(index) * stage.RunMinutes + stage.TeardownMinutes);
    }
}

public sealed record LeaderDemoScaleStage(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    int RunMinutes,
    int TeardownMinutes);
