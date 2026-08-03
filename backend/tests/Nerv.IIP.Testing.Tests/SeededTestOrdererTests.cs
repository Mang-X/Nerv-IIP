using Nerv.IIP.Testing.Xunit;

namespace Nerv.IIP.Testing.Tests;

public sealed class SeededTestOrdererTests
{
    private static readonly string[] FullyQualifiedNames =
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
    public void OrderFullyQualifiedNames_ReturnsTheSameOrderForTheSameSeed()
    {
        var first = SeededTestOrdering.OrderFullyQualifiedNames(FullyQualifiedNames, "man662-01");
        var second = SeededTestOrdering.OrderFullyQualifiedNames(FullyQualifiedNames.Reverse(), "man662-01");

        Assert.Equal(first, second);
    }

    [Fact]
    public void OrderFullyQualifiedNames_ReturnsDifferentOrdersForTheTwoFixedSeeds()
    {
        var first = SeededTestOrdering.OrderFullyQualifiedNames(FullyQualifiedNames, "man662-01");
        var second = SeededTestOrdering.OrderFullyQualifiedNames(FullyQualifiedNames, "man662-02");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OrderFullyQualifiedNames_UsesTheFixedDefaultWhenTheSeedIsMissing()
    {
        var missing = SeededTestOrdering.OrderFullyQualifiedNames(FullyQualifiedNames, null);
        var explicitDefault = SeededTestOrdering.OrderFullyQualifiedNames(FullyQualifiedNames, "nerv-iip-default");

        Assert.Equal(explicitDefault, missing);
    }
}
