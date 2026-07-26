namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L0 主数据的 ProductEngineering 侧固定形状（设定集 §4）：
/// 24 个成品，每个 1 条 EBOM（9 行）+ 1 条 MBOM（11 行）+ 1 条 8 道工序的工艺路线 +
/// 生产版本（全部 V1；热销 8 款另有 V2「换弹簧供应商」的版本演进）。
/// 物料/工作中心编码与 MasterData 侧 <c>WorldBibleSpec</c> 按同一字面量重复声明，
/// 两侧各有黄金向量测试防止漂移；号段（<c>FG-/SF-/RM-/PK-</c>）与固定演示事实
/// （<c>*-DEMO-*</c>）、规模块（<c>*-SCALE-*</c>）完全隔离。
/// </summary>
public static class WorldBibleSpec
{
    /// <summary>平台上线日 = 设定集 §1 的 2026-01-05；V1 自上线日生效。</summary>
    public static readonly DateOnly V1EffectiveDate = new(2026, 1, 5);

    /// <summary>热销 8 款的 V2（换弹簧供应商）自 2026-07-01 生效，V1 同日前一天失效。</summary>
    public static readonly DateOnly V2EffectiveDate = new(2026, 7, 1);

    /// <summary>热销 8 款的 V1 在 V2 生效前一天失效，形成可讲述的版本演进。</summary>
    public static readonly DateOnly HotV1ValidTo = new(2026, 6, 30);

    public const string V1Revision = "1";
    public const string V2Revision = "2";

    /// <summary>V2 使用的二供弹簧（前/后），设定集 §4「换弹簧供应商的版本演进故事」。</summary>
    public const string SecondSourceFrontSpringSku = "RM-SPR-05";
    public const string SecondSourceRearSpringSku = "RM-SPR-06";

    public const string LabelSkuCode = "PK-LBL-03";

    /// <summary>6 车型平台（设定集 §4）。</summary>
    public static readonly WorldBiblePlatform[] Platforms =
    [
        new("P1", "A 级轿车 P1"),
        new("P2", "A 级轿车 P2"),
        new("S1", "SUV S1"),
        new("S2", "SUV S2"),
        new("M1", "MPV M1"),
        new("E1", "新能源 E1"),
    ];

    /// <summary>热销平台：其 4 个变体（前/后 × 左/右）合计 8 款有 V2。</summary>
    public static readonly string[] HotPlatformCodes = ["P1", "S1"];

    /// <summary>8 道标准工序（设定集 §4：下料→CNC 精车→精磨→阀系预装→总装→电泳→性能终检→包装）。</summary>
    public static readonly IReadOnlyList<WorldBibleStandardOperation> StandardOperations =
    [
        new(10, "OP-WB-CUT", "下料", "WC-TUB-01", 15, 2, 5, false),
        new(20, "OP-WB-CNC", "CNC 精车", "WC-ROD-01", 20, 6, 5, false),
        new(30, "OP-WB-GRD", "精磨", "WC-GRD-01", 12, 4, 4, false),
        new(40, "OP-WB-VLV", "阀系预装", "WC-VA-01", 8, 3, 3, false),
        new(50, "OP-WB-ASM", "总装", "WC-FA-01", 10, 5, 4, false),
        new(60, "OP-WB-CTG", "电泳涂装", "WC-CT-01", 25, 3, 8, false),
        new(70, "OP-WB-TST", "性能终检", "WC-TS-01", 6, 2, 2, true),
        new(80, "OP-WB-PKG", "包装", "WC-PK-01", 5, 1, 2, false),
    ];

    /// <summary>24 个成品的完整工程形状，按平台 × 类型 × 左右确定性展开。</summary>
    public static readonly IReadOnlyList<WorldBibleProduct> Products = BuildProducts();

    public static string EngineeringBomCode(string skuCode) => $"EBOM-{skuCode}";

    public static string ManufacturingBomCode(string skuCode) => $"MBOM-{skuCode}";

    public static string RoutingCode(string skuCode) => $"ROUTING-{skuCode}";

    public static string VersionId(string code, string revision) => $"{code}:{revision}";

    private static IReadOnlyList<WorldBibleProduct> BuildProducts()
    {
        var products = new List<WorldBibleProduct>(24);
        for (var platformIndex = 0; platformIndex < Platforms.Length; platformIndex++)
        {
            var platform = Platforms[platformIndex];
            var isHot = HotPlatformCodes.Contains(platform.Code, StringComparer.Ordinal);
            foreach (var isFront in new[] { true, false })
            {
                foreach (var side in new[] { "L", "R" })
                {
                    var typeSegment = isFront ? "QJ" : "HJ";
                    var typeName = isFront ? "前滑柱总成" : "后减振器总成";
                    var sideName = side == "L" ? "左" : "右";
                    var componentIndex = isFront ? platformIndex : (platformIndex + 3) % 6;
                    products.Add(new WorldBibleProduct(
                        SkuCode: $"FG-{typeSegment}-{platform.Code}-{side}",
                        SkuName: $"{platform.Code} 平台{typeName}（{sideName}）",
                        PlatformCode: platform.Code,
                        IsFront: isFront,
                        Side: side,
                        IsHotSelling: isHot,
                        PistonRodSkuCode: $"SF-ROD-{componentIndex + 1:D2}",
                        CylinderTubeSkuCode: $"SF-TUB-{componentIndex + 1:D2}",
                        ValveAssemblySkuCode: $"SF-VLV-{platformIndex + 1:D2}",
                        SpringSkuCode: $"RM-SPR-{(platformIndex % 4) + 1:D2}",
                        OilSealSkuCode: $"RM-SEL-{(platformIndex % 4) + 1:D2}",
                        DamperOilSkuCode: isFront ? "RM-OIL-01" : "RM-OIL-02",
                        ConnectingRingSkuCode: $"RM-ACC-{(platformIndex % 3) + 1:D2}",
                        DustCoverSkuCode: $"RM-ACC-{4 + (platformIndex % 3):D2}",
                        FastenerSkuCode: $"RM-ACC-{7 + (platformIndex % 4):D2}",
                        CartonSkuCode: $"PK-BOX-{(platformIndex % 4) + 1:D2}",
                        CutWorkCenterCode: $"WC-TUB-{(platformIndex % 2) + 1:D2}",
                        MachiningWorkCenterCode: $"WC-ROD-{(platformIndex % 2) + 1:D2}",
                        AssemblyWorkCenterCode: isFront
                            ? $"WC-FA-{(platformIndex % 3) + 1:D2}"
                            : $"WC-RA-{(platformIndex % 2) + 1:D2}"));
                }
            }
        }

        return [.. products];
    }
}

public sealed record WorldBiblePlatform(string Code, string Name);

public sealed record WorldBibleStandardOperation(
    int Sequence,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int SetupMinutes,
    int RunMinutes,
    int TeardownMinutes,
    bool RequiresQualityInspection);

public sealed record WorldBibleProduct(
    string SkuCode,
    string SkuName,
    string PlatformCode,
    bool IsFront,
    string Side,
    bool IsHotSelling,
    string PistonRodSkuCode,
    string CylinderTubeSkuCode,
    string ValveAssemblySkuCode,
    string SpringSkuCode,
    string OilSealSkuCode,
    string DamperOilSkuCode,
    string ConnectingRingSkuCode,
    string DustCoverSkuCode,
    string FastenerSkuCode,
    string CartonSkuCode,
    string CutWorkCenterCode,
    string MachiningWorkCenterCode,
    string AssemblyWorkCenterCode)
{
    /// <summary>V2（热销款）改用二供弹簧，其余物料不变。</summary>
    public string SecondSourceSpringSkuCode => IsFront
        ? WorldBibleSpec.SecondSourceFrontSpringSku
        : WorldBibleSpec.SecondSourceRearSpringSku;

    /// <summary>该成品在指定修订下的弹簧物料。</summary>
    public string SpringSkuCodeFor(string revision) =>
        revision == WorldBibleSpec.V2Revision ? SecondSourceSpringSkuCode : SpringSkuCode;

    /// <summary>EBOM 9 行：3 个半成品 + 6 项原材料（设定集 §4「8–12 行真实层次」）。</summary>
    public IReadOnlyList<WorldBibleBomLine> EngineeringLines(string revision) =>
    [
        new(PistonRodSkuCode, 1m, "pcs", 0m),
        new(CylinderTubeSkuCode, 1m, "pcs", 0m),
        new(ValveAssemblySkuCode, 1m, "pcs", 0m),
        new(SpringSkuCodeFor(revision), 1m, "pcs", 0.005m),
        new(OilSealSkuCode, 2m, "pcs", 0.01m),
        new(DamperOilSkuCode, 0.65m, "l", 0.02m),
        new(ConnectingRingSkuCode, 2m, "pcs", 0.005m),
        new(DustCoverSkuCode, 1m, "pcs", 0.005m),
        new(FastenerSkuCode, 4m, "pcs", 0.01m),
    ];

    /// <summary>MBOM 11 行 = EBOM 9 行 + 纸箱 + 成品箱贴标签。</summary>
    public IReadOnlyList<WorldBibleBomLine> ManufacturingLines(string revision) =>
    [
        .. EngineeringLines(revision),
        new(CartonSkuCode, 0.25m, "pcs", 0.01m),
        new(WorldBibleSpec.LabelSkuCode, 1m, "pcs", 0.02m),
    ];

    /// <summary>8 道工序及其工作中心归属（跨三车间流转）。</summary>
    public IReadOnlyList<WorldBibleRoutingStage> RoutingStages()
    {
        var workCenters = new[]
        {
            CutWorkCenterCode,
            MachiningWorkCenterCode,
            "WC-GRD-01",
            "WC-VA-01",
            AssemblyWorkCenterCode,
            "WC-CT-01",
            "WC-TS-01",
            "WC-PK-01",
        };
        return [.. WorldBibleSpec.StandardOperations.Select((operation, index) =>
            new WorldBibleRoutingStage(operation, workCenters[index]))];
    }
}

public sealed record WorldBibleBomLine(
    string ComponentSkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate);

public sealed record WorldBibleRoutingStage(
    WorldBibleStandardOperation Operation,
    string WorkCenterCode);
