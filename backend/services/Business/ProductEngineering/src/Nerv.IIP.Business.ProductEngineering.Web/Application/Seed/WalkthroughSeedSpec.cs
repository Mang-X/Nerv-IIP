namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

public static class WalkthroughSeedSpec
{
    public const string FinishedSkuCode = "FG-QJ-P1-L";
    public const string RodSkuCode = "SF-ROD-01";
    public const string RodName = "活塞杆 φ20×380";
    public const string RodRawMaterialSkuCode = "RM-BAR-01";
    public const decimal RodRawMaterialKilogramsPerPiece = 1.40m;

    public static readonly IReadOnlyList<WorldBibleStandardOperation> RodOperations =
        [.. new[] { "OP-WB-CUT", "OP-WB-CNC", "OP-WB-GRD" }.Select(code =>
            WorldBibleSpec.StandardOperations.Single(operation => operation.OperationCode == code))];

    public static readonly WorldBibleProduct Product = WorldBibleSpec.Products.Single(
        product => string.Equals(product.SkuCode, FinishedSkuCode, StringComparison.Ordinal));
}
