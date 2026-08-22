namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

public static class WalkthroughSeedSpec
{
    public const string FinishedSkuCode = "FG-QJ-P1-L";

    public static readonly WorldBibleProduct Product = WorldBibleSpec.Products.Single(
        product => string.Equals(product.SkuCode, FinishedSkuCode, StringComparison.Ordinal));
}
