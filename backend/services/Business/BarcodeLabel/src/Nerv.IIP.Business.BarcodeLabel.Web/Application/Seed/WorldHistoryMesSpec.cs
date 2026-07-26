namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;

/// <summary>
/// L1 背景历史的 **MES 独有**形状：工艺路线展开、工序任务、齐套需求、领料、报工人员与补产工单。
/// 销售侧的跨服务共享形状在 <see cref="WorldHistorySpec"/>。
///
/// 工序、工作中心、物料、人员编码全部**引用** L0（`WorldBibleSpec`，PR #1124）的既有实体，
/// 本引擎不新建任何主数据。工作中心归属按 L0 ProductEngineering 侧的 <c>RoutingStages()</c>
/// 同一公式重算，黄金向量测试锁定不漂移。
/// </summary>
public static class WorldHistoryMesSpec
{
    /// <summary>L0 §4 的 6 个车型平台，顺序即 platformIndex。</summary>
    public static readonly string[] PlatformCodes = ["P1", "P2", "S1", "S2", "M1", "E1"];

    /// <summary>L0 §4 的 8 道标准工序（下料→CNC 精车→精磨→阀系预装→总装→电泳→性能终检→包装）。</summary>
    public static readonly IReadOnlyList<WorldHistoryOperation> StandardOperations =
    [
        new(10, "OP-WB-CUT", "下料", 15, 2, 5, false, WorldHistoryWorkshop.Machining),
        new(20, "OP-WB-CNC", "CNC 精车", 20, 6, 5, false, WorldHistoryWorkshop.Machining),
        new(30, "OP-WB-GRD", "精磨", 12, 4, 4, false, WorldHistoryWorkshop.Machining),
        new(40, "OP-WB-VLV", "阀系预装", 8, 3, 3, false, WorldHistoryWorkshop.Assembly),
        new(50, "OP-WB-ASM", "总装", 10, 5, 4, false, WorldHistoryWorkshop.Assembly),
        new(60, "OP-WB-CTG", "电泳涂装", 25, 3, 8, false, WorldHistoryWorkshop.Surface),
        new(70, "OP-WB-TST", "性能终检", 6, 2, 2, true, WorldHistoryWorkshop.Surface),
        new(80, "OP-WB-PKG", "包装", 5, 1, 2, false, WorldHistoryWorkshop.Surface),
    ];

    /// <summary>
    /// 可跳过的两道工序（设定集 §7「每单 6–8 工序任务」）：
    /// 下料在直接投半成品时跳过，阀系预装在阀系外购已预装时跳过。核心 6 道恒在。
    /// </summary>
    public const int CuttingSequence = 10;
    public const int ValvePreAssemblySequence = 40;
    public const double CuttingIncludedProbability = 0.70;
    public const double ValvePreAssemblyIncludedProbability = 0.65;

    /// <summary>需要质检的工序序号——二期质量域接管检验任务时的预留引用点。</summary>
    public const int QualityInspectionSequence = 70;

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

    #region L0 §4 物料（齐套 / 领料引用点）

    /// <summary>某成品的 4 项主料，与 L0 ProductEngineering 的 MBOM 前四行同码同序。</summary>
    public static IReadOnlyList<WorldHistoryComponent> Components(string skuCode)
    {
        var platformIndex = PlatformIndex(skuCode);
        var componentIndex = IsFrontStrut(skuCode) ? platformIndex : (platformIndex + 3) % 6;
        return
        [
            new($"SF-ROD-{componentIndex + 1:D2}", 1m, "pcs"),
            new($"SF-TUB-{componentIndex + 1:D2}", 1m, "pcs"),
            new($"SF-VLV-{platformIndex + 1:D2}", 1m, "pcs"),
            new($"RM-SPR-{(platformIndex % 4) + 1:D2}", 1m, "pcs"),
        ];
    }

    #endregion

    #region L0 §5 班组成员（报工操作员）

    /// <summary>
    /// L0 的 25 名现场班组成员：6 名班组长（EMP-004..009）+ 19 名操作工（EMP-010..028）。
    /// 车间归属按 L0 <c>WorldBibleSpec.BuildEmployees()</c> 的同一轮转公式重算。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryOperator> Operators = BuildOperators();

    private static IReadOnlyList<WorldHistoryOperator> BuildOperators()
    {
        // L0 班组顺序：MC-A、MC-B、AS-A、AS-B、SP-A、SP-B（三车间各早/中班）。
        var teamWorkshops = new[]
        {
            WorldHistoryWorkshop.Machining, WorldHistoryWorkshop.Machining,
            WorldHistoryWorkshop.Assembly, WorldHistoryWorkshop.Assembly,
            WorldHistoryWorkshop.Surface, WorldHistoryWorkshop.Surface,
        };
        var teamCodes = new[] { "TEAM-WB-MC-A", "TEAM-WB-MC-B", "TEAM-WB-AS-A", "TEAM-WB-AS-B", "TEAM-WB-SP-A", "TEAM-WB-SP-B" };

        var operators = new List<WorldHistoryOperator>(25);

        // 6 名班组长：L0 序号 3..8（0 基），每人一个班组。
        for (var leaderIndex = 0; leaderIndex < 6; leaderIndex++)
        {
            var ordinal = 3 + leaderIndex;
            operators.Add(new WorldHistoryOperator(
                $"user-emp-{ordinal + 1:D3}",
                $"EMP-{ordinal + 1:D3}",
                teamCodes[leaderIndex],
                teamWorkshops[leaderIndex],
                // L0 班组顺序里偶数下标是早班、奇数是中班。
                ShiftIndex: leaderIndex % 2));
        }

        // 19 名操作工：L0 序号 9..27（0 基），前 18 人按 6 个班组轮转，第 19 人补入末组。
        for (var operatorIndex = 0; operatorIndex < 19; operatorIndex++)
        {
            var ordinal = 9 + operatorIndex;
            var teamIndex = operatorIndex < 18 ? operatorIndex % 6 : 5;
            operators.Add(new WorldHistoryOperator(
                $"user-emp-{ordinal + 1:D3}",
                $"EMP-{ordinal + 1:D3}",
                teamCodes[teamIndex],
                teamWorkshops[teamIndex],
                ShiftIndex: teamIndex % 2));
        }

        return operators;
    }

    /// <summary>按车间取可用报工人员（班组长也报工，符合小厂实际）。</summary>
    public static IReadOnlyList<WorldHistoryOperator> OperatorsIn(WorldHistoryWorkshop workshop) =>
        [.. Operators.Where(x => x.Workshop == workshop)];

    #endregion

    #region 补产工单（设定集 §7「约 3600 张工单，含内部补产」）

    /// <summary>补产比例：3200 张订单工单 + 12.5% 补产 ≈ 3600 张（设定集 §7）。</summary>
    public const double ReworkWorkOrderRatio = 0.125;

    /// <summary>补产工单号仍在 §9 的 <c>WO-2026-*</c> 段内，用 R 前缀与订单工单区分。</summary>
    public static string ReworkWorkOrderNo(int sequence) => $"WO-2026-R{sequence:D4}";

    #endregion

    /// <summary>
    /// 单张工单的确定性形状：工序子集、投料放大量、报工次数与报废量。
    ///
    /// 关键不变量：<c>好品产出 == 订单数量</c>。投料按报废量放大（工单数量 = 订单数量 + 报废量），
    /// 于是「工单完工 → 完工入库 → 发货」的数量链逐件对得上，不会出现少发货。
    /// </summary>
    public static WorldHistoryWorkOrderPlan BuildWorkOrderPlan(string workOrderNo, string skuCode, decimal goodQuantity)
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

        // 约 30% 的工单有报废（设定集 §7 不合格率 2.3% 的量级），报废量为好品量的 1%–3%。
        var scrapQuantity = random.Chance(0.30)
            ? Math.Max(1m, decimal.Round(goodQuantity * (random.NextInt(1, 4) / 100m), 0, MidpointRounding.AwayFromZero))
            : 0m;

        return new WorldHistoryWorkOrderPlan(
            WorkOrderNo: workOrderNo,
            SkuCode: skuCode,
            GoodQuantity: goodQuantity,
            ScrapQuantity: scrapQuantity,
            OperationSequences: sequences,
            ReportCount: random.NextInt(2, 6),
            SplitMaterialIssue: random.Chance(0.40));
    }

    /// <summary>工序任务的计划工时：准备 + 单件工时 × 数量 + 收尾。</summary>
    public static TimeSpan OperationDuration(WorldHistoryOperation operation, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var minutes = operation.SetupMinutes + (double)quantity * operation.RunMinutesPerUnit + operation.TeardownMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    public static WorldHistoryOperation Operation(int sequence) =>
        StandardOperations.Single(x => x.Sequence == sequence);

    #region 号段

    public static string OperationTaskId(string workOrderNo, int sequence) => $"{workOrderNo}-OP-{sequence:D2}";
    public static string MaterialIssueRequestNo(string workOrderNo, int ordinal) => $"MIR-{workOrderNo}-{ordinal:D2}";
    public static string ProductionReportNo(string workOrderNo, int ordinal) => $"RPT-{workOrderNo}-{ordinal:D2}";
    public static string FinishedGoodsReceiptNo(string workOrderNo) => $"FGR-{workOrderNo}";
    public static string ProducedLotNo(string workOrderNo) => $"LOT-{workOrderNo}";

    #endregion
}

/// <summary>L0 §2 的三个车间，决定报工人员池与工序归属。</summary>
public enum WorldHistoryWorkshop
{
    /// <summary>一车间 · 机加车间（WS-01）。</summary>
    Machining,

    /// <summary>二车间 · 装配车间（WS-02）。</summary>
    Assembly,

    /// <summary>三车间 · 表面与包装车间（WS-03）。</summary>
    Surface,
}

public sealed record WorldHistoryOperation(
    int Sequence,
    string OperationCode,
    string OperationName,
    int SetupMinutes,
    double RunMinutesPerUnit,
    int TeardownMinutes,
    bool RequiresQualityInspection,
    WorldHistoryWorkshop Workshop);

public sealed record WorldHistoryComponent(string SkuCode, decimal QuantityPer, string UomCode);

public sealed record WorldHistoryOperator(
    string UserId,
    string EmployeeNo,
    string TeamCode,
    WorldHistoryWorkshop Workshop,
    int ShiftIndex);

public sealed record WorldHistoryWorkOrderPlan(
    string WorkOrderNo,
    string SkuCode,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    IReadOnlyList<int> OperationSequences,
    int ReportCount,
    bool SplitMaterialIssue)
{
    /// <summary>工单投料数量 = 好品数量 + 报废数量。</summary>
    public decimal WorkOrderQuantity => GoodQuantity + ScrapQuantity;

    /// <summary>本工单是否包含需要质检的性能终检工序（二期质量域的引用点）。</summary>
    public bool RequiresQualityInspection =>
        OperationSequences.Contains(WorldHistoryMesSpec.QualityInspectionSequence);
}
