using System.Reflection;
using Xunit;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class PlatformGatewayTestIsolationTests
{
    [Fact]
    public void Web_tests_disable_parallelization_for_shared_gateway_test_server_state()
    {
        var behavior = typeof(PlatformGatewayTestIsolationTests).Assembly
            .GetCustomAttributes<CollectionBehaviorAttribute>()
            .Single();

        Assert.True(behavior.DisableTestParallelization);
    }
}
