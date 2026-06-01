using System.Reflection;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayTestIsolationTests
{
    [Fact]
    public void Assembly_ShouldDisableXunitParallelization()
    {
        var attributes = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<CollectionBehaviorAttribute>()
            .ToArray();

        var behavior = Assert.Single(attributes);
        Assert.NotNull(behavior);
        Assert.True(behavior.DisableTestParallelization);
    }
}
