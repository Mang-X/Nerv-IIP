using System.Reflection;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayTestIsolationTests
{
    [Fact]
    public void Test_assembly_disables_xunit_parallelization()
    {
        var behavior = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<CollectionBehaviorAttribute>()
            .SingleOrDefault();

        Assert.NotNull(behavior);
        Assert.True(behavior.DisableTestParallelization);
    }
}
