namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

public static class WalkthroughSeedSpec
{
    public const string FinishedSkuCode = "FG-QJ-P1-L";
    public const string RodSkuCode = "SF-ROD-01";
    public const string RodName = "活塞杆 φ20×380";
    public const string RodRawMaterialSkuCode = "RM-BAR-01";
    public const decimal RodRawMaterialKilogramsPerPiece = 1.40m;

    public static readonly DateOnly V1EffectiveDate = new(2026, 1, 5);

    public static readonly DateOnly V2EffectiveDate = new(2026, 7, 1);

    public static readonly DateOnly V1ValidTo = new(2026, 6, 30);

    public const string V1Revision = "1";
    public const string V2Revision = "2";


    public static readonly IReadOnlyList<WalkthroughStandardOperation> StandardOperations =
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

    public static IReadOnlyList<WalkthroughStandardOperation> RodOperations { get; } =
        [StandardOperations[0], StandardOperations[1], StandardOperations[2]];

    public static WalkthroughProduct Product { get; } = new();

    public static string EngineeringBomCode(string skuCode) => $"EBOM-{skuCode}";

    public static string ManufacturingBomCode(string skuCode) => $"MBOM-{skuCode}";

    public static string RoutingCode(string skuCode) => $"ROUTING-{skuCode}";

    public static string VersionId(string code, string revision) => $"{code}:{revision}";

}

public sealed record WalkthroughStandardOperation(
    int Sequence,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int SetupMinutes,
    int RunMinutes,
    int TeardownMinutes,
    bool RequiresQualityInspection);

public sealed class WalkthroughProduct
{
    public string SkuCode => WalkthroughSeedSpec.FinishedSkuCode;
    public string SkuName => "P1 平台前滑柱总成（左）";

    public IReadOnlyList<WalkthroughBomLine> EngineeringLines(string revision) =>
    [
        new(WalkthroughSeedSpec.RodSkuCode, 1m, "pcs", 0m),
        new("SF-TUB-01", 1m, "pcs", 0m),
        new("SF-VLV-01", 1m, "pcs", 0m),
        new(revision == WalkthroughSeedSpec.V2Revision ? "RM-SPR-05" : "RM-SPR-01", 1m, "pcs", 0.005m),
        new("RM-SEL-01", 2m, "pcs", 0.01m),
        new("RM-OIL-01", 0.65m, "l", 0.02m),
        new("RM-ACC-01", 2m, "pcs", 0.005m),
        new("RM-ACC-04", 1m, "pcs", 0.005m),
        new("RM-ACC-07", 4m, "pcs", 0.01m),
    ];

    public IReadOnlyList<WalkthroughBomLine> ManufacturingLines(string revision) =>
    [
        .. EngineeringLines(revision),
        new("PK-BOX-01", 0.25m, "pcs", 0.01m),
        new("PK-LBL-03", 1m, "pcs", 0.02m),
    ];

    public IReadOnlyList<WalkthroughRoutingStage> RoutingStages() =>
        [.. WalkthroughSeedSpec.StandardOperations.Select(operation =>
            new WalkthroughRoutingStage(operation, operation.DefaultWorkCenterCode))];
}

public sealed record WalkthroughBomLine(
    string ComponentSkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate);

public sealed record WalkthroughRoutingStage(
    WalkthroughStandardOperation Operation,
    string WorkCenterCode);
