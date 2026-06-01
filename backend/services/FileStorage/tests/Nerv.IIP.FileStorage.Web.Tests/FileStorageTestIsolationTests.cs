using System.Reflection;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageTestIsolationTests
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
