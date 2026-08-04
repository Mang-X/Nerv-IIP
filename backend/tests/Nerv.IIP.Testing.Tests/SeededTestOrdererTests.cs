using Nerv.IIP.Testing.Xunit;

namespace Nerv.IIP.Testing.Tests;

public sealed class SeededTestOrdererTests
{
    private static readonly string[] DisplayNames =
    [
        "Nerv.IIP.Tests.AlphaTests.First",
        "Nerv.IIP.Tests.AlphaTests.Second",
        "Nerv.IIP.Tests.BetaTests.First",
        "Nerv.IIP.Tests.BetaTests.Second",
        "Nerv.IIP.Tests.GammaTests.First",
        "Nerv.IIP.Tests.GammaTests.Second",
        "Nerv.IIP.Tests.DeltaTests.First",
        "Nerv.IIP.Tests.DeltaTests.Second",
    ];

    [Fact]
    public void OrderDisplayNames_ReturnsTheSameOrderForTheSameSeed()
    {
        var first = SeededTestOrdering.OrderDisplayNames(DisplayNames, "man662-01");
        var second = SeededTestOrdering.OrderDisplayNames(DisplayNames.Reverse(), "man662-01");

        Assert.Equal(first, second);
    }

    [Fact]
    public void OrderDisplayNames_ReturnsDifferentOrdersForTheTwoFixedSeeds()
    {
        var first = SeededTestOrdering.OrderDisplayNames(DisplayNames, "man662-01");
        var second = SeededTestOrdering.OrderDisplayNames(DisplayNames, "man662-02");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OrderDisplayNames_UsesTheFixedDefaultWhenTheSeedIsMissing()
    {
        var missing = SeededTestOrdering.OrderDisplayNames(DisplayNames, null);
        var explicitDefault = SeededTestOrdering.OrderDisplayNames(DisplayNames, "nerv-iip-default");

        Assert.Equal(explicitDefault, missing);
    }
}
