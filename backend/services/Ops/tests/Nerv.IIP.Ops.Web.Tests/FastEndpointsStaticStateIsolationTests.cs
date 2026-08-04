using System.Text.Json;
using FastEndpoints;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class FastEndpointsStaticStateIsolationTests
{
    // Counterpart to Nerv.IIP.FastEndpoints.ProcessIsolation.Tests, which proves the mutation is
    // process-wide and unrecoverable. This asserts the other half of the contract: an ordinary test
    // assembly never observes that mutation, because the sacrificial mutation lives in its own process.
    [Fact]
    public void Ordinary_test_assembly_never_observes_the_sacrificial_serializer_mutation()
    {
        var config = new Config();

        Assert.NotSame(JsonNamingPolicy.SnakeCaseLower, config.Serializer.Options.PropertyNamingPolicy);
        Assert.DoesNotContain(
            config.Serializer.Options.Converters,
            converter => converter.GetType().Name.Contains("TestOnly", StringComparison.Ordinal));
    }
}
