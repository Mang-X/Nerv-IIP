using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

// DomainInvariant / Regression: #4108 requires identity before lot splitting,
// independent even when both netting stages have identical sources and explanations.
public sealed class NetRequirementIdentityTests
{
    internal static readonly DateOnly Date = new(2026, 10, 10);

    internal static MrpCalculationInput SplitInput() => new(
        "org-a", "env-a", Date, Date,
        [new("SO-30", "SKU", "pcs", "SITE", 30m, Date, "sales-order")],
        [], [], [], [], [new("SKU", "pcs", "SITE", 0, 0m, null, 12m, null, "buy")], []);

    internal static MrpCalculationInput CollisionInput() => new(
        "org-a", "env-a", Date, Date,
        [new("SKU", "SKU", "pcs", "SITE", 3m, Date, "safety-stock")],
        [], [], [], [], [new("SKU", "pcs", "SITE", 0, 3m, null, null, null, "buy")], []);

    internal static MrpCalculationInput ComponentInput() => new(
        "org-a", "env-a", Date, Date,
        [new("SO-5", "PARENT", "pcs", "SITE", 5m, Date, "sales-order")],
        [], [new("PARENT", "PV", "BOM", "ROUTE")],
        [new("PARENT", "COMPONENT", "pcs", 1m)], [],
        [new("PARENT", "pcs", "SITE", 0, 3m, null, null, null, "make"),
         new("COMPONENT", "pcs", "SITE", 0, 0m, null, null, null, "buy")], []);

    private static Guid Reference(CalculatedPlanningSuggestion suggestion) =>
        Assert.IsType<Guid>(suggestion.NetRequirementReference);

    [Fact]
    public void Split_batches_share_identity_and_repeated_netting_is_independent()
    {
        var first = MrpCalculator.Calculate(SplitInput()).ToArray();
        Assert.Equal([12m, 12m, 6m], first.Select(x => x.Quantity).ToArray());
        Assert.All(first, x => Assert.Equal(30m, x.NetRequirementExplanation.NetRequirementQuantity));
        var reference = Assert.Single(first.Select(Reference).Distinct());
        Assert.NotEqual(Guid.Empty, reference);
        Assert.NotEqual(reference, Assert.Single(MrpCalculator.Calculate(SplitInput()).Select(Reference).Distinct()));
    }

    [Fact]
    public void Identical_sources_and_explanations_do_not_merge_independent_stages()
    {
        var suggestions = MrpCalculator.Calculate(CollisionInput()).ToArray();
        Assert.Equal(2, suggestions.Length);
        Assert.All(suggestions, x => Assert.Equal(3m, x.Quantity));
        Assert.Equal(suggestions[0].PeggingLinks.ToArray(), suggestions[1].PeggingLinks.ToArray());
        Assert.Equal(suggestions[0].NetRequirementExplanation, suggestions[1].NetRequirementExplanation);
        Assert.NotEqual(Reference(suggestions[0]), Reference(suggestions[1]));
    }

    [Fact]
    public void Sales_and_automatic_reserve_component_netting_have_separate_identity()
    {
        var components = MrpCalculator.Calculate(ComponentInput()).Where(x => x.SkuCode == "COMPONENT").ToArray();
        Assert.Equal([5m, 3m], components.Select(x => x.Quantity).ToArray());
        Assert.NotEqual(Reference(components[0]), Reference(components[1]));
    }
}
