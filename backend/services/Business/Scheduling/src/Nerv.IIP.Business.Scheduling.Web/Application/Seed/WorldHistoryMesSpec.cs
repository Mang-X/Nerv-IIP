namespace Nerv.IIP.Business.Scheduling.Web.Application.Seed;

/// <summary>
/// L1 背景历史的 **MES 镜像形状**：工艺路线展开、工序任务号、工作中心归属与设备池。
///
/// 排产库 <c>nerv_iip_scheduling</c> 不允许跨库查 <c>nerv_iip_mes</c>，也不允许跨 schema 外键，
/// 因此这里按与 MES / Quality 侧**同一确定性纯函数**重算工序与工作中心归属：
/// 工序号 <c>{WO-2026-#####}-OP-{seq}</c>、工作中心 <c>WC-*</c> 与 MES <c>operation_tasks</c> 逐字对得上，
/// 无需通信即可让「排产工作台里的工序」与「车间里的工序任务」是同一件事。
///
/// 设备池 <c>DEV-*</c> 是排产侧独有的一层：MES 的 <c>device_asset_id</c> 段按工作中心切分，
/// 每个工作中心固定若干台设备，排产在其中做有限产能分配。
/// </summary>
public static class WorldHistoryMesSpec
{
    /// <summary>L0 §4 的 6 个车型平台，顺序即 platformIndex。</summary>
    public static readonly string[] PlatformCodes = ["P1", "P2", "S1", "S2", "M1", "E1"];

    /// <summary>L0 §4 的 8 道标准工序（下料→CNC 精车→精磨→阀系预装→总装→电泳→性能终检→包装）。</summary>
    public static readonly IReadOnlyList<WorldHistoryOperation> StandardOperations =
    [
        new(10, "OP-WB-CUT", "下料", 15, 2, 5, false),
        new(20, "OP-WB-CNC", "CNC 精车", 20, 6, 5, false),
        new(30, "OP-WB-GRD", "精磨", 12, 4, 4, false),
        new(40, "OP-WB-VLV", "阀系预装", 8, 3, 3, false),
        new(50, "OP-WB-ASM", "总装", 10, 5, 4, false),
        new(60, "OP-WB-CTG", "电泳涂装", 25, 3, 8, false),
        new(70, "OP-WB-TST", "性能终检", 6, 2, 2, true),
        new(80, "OP-WB-PKG", "包装", 5, 1, 2, false),
    ];

    /// <summary>
    /// 可跳过的两道工序（设定集 §7「每单 6–8 工序任务」）：
    /// 下料在直接投半成品时跳过，阀系预装在阀系外购已预装时跳过。核心 6 道恒在。
    /// </summary>
    public const int CuttingSequence = 10;
    public const int ValvePreAssemblySequence = 40;
    public const double CuttingIncludedProbability = 0.70;
    public const double ValvePreAssemblyIncludedProbability = 0.65;

    #region L0 工作中心归属（与 ProductEngineering 侧 RoutingStages() 同一公式）

    public static int PlatformIndex(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        for (var index = 0; index < PlatformCodes.Length; index++)
        {
            if (skuCode.Contains($"-{PlatformCodes[index]}-", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    public static bool IsFrontStrut(string skuCode) =>
        skuCode.StartsWith("FG-QJ-", StringComparison.Ordinal);

    /// <summary>某成品第 <paramref name="sequence"/> 道工序落在哪个工作中心。</summary>
    public static string WorkCenterCode(string skuCode, int sequence)
    {
        var platformIndex = PlatformIndex(skuCode);
        return sequence switch
        {
            10 => $"WC-TUB-{(platformIndex % 2) + 1:D2}",
            20 => $"WC-ROD-{(platformIndex % 2) + 1:D2}",
            30 => "WC-GRD-01",
            40 => "WC-VA-01",
            50 => IsFrontStrut(skuCode)
                ? $"WC-FA-{(platformIndex % 3) + 1:D2}"
                : $"WC-RA-{(platformIndex % 2) + 1:D2}",
            60 => "WC-CT-01",
            70 => "WC-TS-01",
            80 => "WC-PK-01",
            _ => throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Unknown world-bible routing sequence."),
        };
    }

    #endregion

    #region 设备池（排产资源）

    /// <summary>
    /// 工作中心 → 设备池。设备号段与 MES <c>operation_tasks.device_asset_id</c> 的实际段一致
    /// （DEV-CNC-01..10 / DEV-GRD-01..04 / DEV-ASM-01..12 / DEV-WLD-01..03 /
    /// DEV-CTG-01..03 / DEV-TST-01..04 / DEV-PKG-01..02 / DEV-AUX-01..08）。
    /// </summary>
    private static readonly Dictionary<string, string[]> WorkCenterResourceMap = new(StringComparer.Ordinal)
    {
        ["WC-TUB-01"] = ["DEV-WLD-01", "DEV-WLD-02"],
        ["WC-TUB-02"] = ["DEV-WLD-03"],
        ["WC-ROD-01"] = ["DEV-CNC-01", "DEV-CNC-02", "DEV-CNC-03", "DEV-CNC-04", "DEV-CNC-05"],
        ["WC-ROD-02"] = ["DEV-CNC-06", "DEV-CNC-07", "DEV-CNC-08", "DEV-CNC-09", "DEV-CNC-10"],
        ["WC-GRD-01"] = ["DEV-GRD-01", "DEV-GRD-02", "DEV-GRD-03", "DEV-GRD-04"],
        ["WC-VA-01"] = ["DEV-AUX-01", "DEV-AUX-02", "DEV-AUX-03", "DEV-AUX-04"],
        ["WC-FA-01"] = ["DEV-ASM-01", "DEV-ASM-02", "DEV-ASM-03"],
        ["WC-FA-02"] = ["DEV-ASM-04", "DEV-ASM-05", "DEV-ASM-06"],
        ["WC-FA-03"] = ["DEV-ASM-07", "DEV-ASM-08"],
        ["WC-RA-01"] = ["DEV-ASM-09", "DEV-ASM-10"],
        ["WC-RA-02"] = ["DEV-ASM-11", "DEV-ASM-12"],
        ["WC-CT-01"] = ["DEV-CTG-01", "DEV-CTG-02", "DEV-CTG-03"],
        ["WC-TS-01"] = ["DEV-TST-01", "DEV-TST-02", "DEV-TST-03", "DEV-TST-04"],
        ["WC-PK-01"] = ["DEV-PKG-01", "DEV-PKG-02"],
    };

    /// <summary>全部工作中心（按编码序，供排产资源清单使用）。</summary>
    public static readonly IReadOnlyList<string> WorkCenterCodes =
        [.. WorkCenterResourceMap.Keys.OrderBy(x => x, StringComparer.Ordinal)];

    /// <summary>该工作中心下的设备池（按编码序）。</summary>
    public static IReadOnlyList<string> ResourcesIn(string workCenterCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workCenterCode);
        return WorkCenterResourceMap.TryGetValue(workCenterCode, out var resources)
            ? resources
            : throw new ArgumentOutOfRangeException(nameof(workCenterCode), workCenterCode, "Unknown world-bible work center.");
    }

    /// <summary>产能瓶颈工作中心：电泳线与性能终检台（单线串行，利用率长期偏高）。</summary>
    public static readonly IReadOnlyList<string> BottleneckWorkCenters = ["WC-CT-01", "WC-TS-01"];

    /// <summary>该工序所需的能力码（与工作中心一一对应，供排产资源匹配）。</summary>
    public static string CapabilityCode(string workCenterCode) => $"CAP-{workCenterCode["WC-".Length..]}";

    /// <summary>工作中心的班次日历号（全厂统一两班制）。</summary>
    public const string CalendarId = "CAL-WB-TWO-SHIFT";

    #endregion

    /// <summary>
    /// 单张工单的确定性工序子集（与 MES 侧 <c>BuildWorkOrderPlan</c> 同一随机流键与同一判定顺序）。
    /// </summary>
    public static IReadOnlyList<int> OperationSequences(string workOrderNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        var random = new WorldHistoryRandom($"workorder:{workOrderNo}");
        var sequences = new List<int>(8);
        foreach (var operation in StandardOperations)
        {
            var included = operation.Sequence switch
            {
                CuttingSequence => random.Chance(CuttingIncludedProbability),
                ValvePreAssemblySequence => random.Chance(ValvePreAssemblyIncludedProbability),
                _ => true,
            };
            if (included)
            {
                sequences.Add(operation.Sequence);
            }
        }

        return sequences;
    }

    /// <summary>
    /// 工序任务的排产时长（分钟）：准备 + 单件工时 × 数量 / 并行工位数 + 收尾，
    /// 夹到 [20, 300] 分钟——单道工序不会长过一个班次（480 分钟），排产才排得进班次窗口。
    /// </summary>
    public const int MinOperationMinutes = 20;
    public const int MaxOperationMinutes = 300;
    private const double ParallelStations = 6.0;

    public static int OperationMinutes(WorldHistoryOperation operation, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var minutes = operation.SetupMinutes
            + ((double)quantity * operation.RunMinutesPerUnit / ParallelStations)
            + operation.TeardownMinutes;
        return Math.Clamp((int)Math.Round(minutes, MidpointRounding.AwayFromZero), MinOperationMinutes, MaxOperationMinutes);
    }

    public static WorldHistoryOperation Operation(int sequence) =>
        StandardOperations.Single(x => x.Sequence == sequence);

    /// <summary>工序任务号：与 MES <c>operation_tasks.operation_task_id</c> 同一公式。</summary>
    public static string OperationTaskId(string workOrderNo, int sequence) => $"{workOrderNo}-OP-{sequence:D2}";
}

public sealed record WorldHistoryOperation(
    int Sequence,
    string OperationCode,
    string OperationName,
    int SetupMinutes,
    double RunMinutesPerUnit,
    int TeardownMinutes,
    bool RequiresQualityInspection);
