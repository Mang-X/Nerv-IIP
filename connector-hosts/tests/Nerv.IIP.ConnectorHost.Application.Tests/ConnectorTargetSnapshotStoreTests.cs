using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ConnectorTargetSnapshotStoreTests
{
    [Fact]
    public void Slow_discovery_does_not_block_an_unrelated_connector_snapshot()
    {
        var blocked = new TaskCompletionSource<IReadOnlyList<ConnectorTarget>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ConnectorTargetSnapshotStore(
            [new BlockingConnector(blocked.Task), new StaticConnector(CreateTarget("healthy"))],
            TimeProvider.System);

        store.TriggerRefresh(CancellationToken.None);

        Assert.Equal("healthy", Assert.Single(store.GetCurrentTargets()).InstanceKey);
        Assert.False(blocked.Task.IsCompleted);
    }

    [Fact]
    public void Signalled_connector_bypasses_refresh_interval_without_rescanning_unrelated_connector()
    {
        var clock = new ControllableTimeProvider();
        var changed = new MutableConnector(CreateTarget("changed") with { HealthStatus = "healthy" });
        var stable = new MutableConnector(CreateTarget("stable"));
        var store = new ConnectorTargetSnapshotStore([changed, stable], clock);
        store.TriggerRefresh(CancellationToken.None);
        changed.Target = changed.Target with { HealthStatus = "unhealthy" };

        store.TriggerRefresh(CancellationToken.None, "changed");

        Assert.Equal("unhealthy", store.GetCurrentTargets().Single(target => target.InstanceKey == "changed").HealthStatus);
        Assert.Equal(2, changed.DiscoveryCount);
        Assert.Equal(1, stable.DiscoveryCount);
    }

    private static ConnectorTarget CreateTarget(string instanceKey) => new(
        "node", "Node", "test", "collector", "Collector", "1.0", instanceKey, instanceKey,
        "running", "healthy", [], new Dictionary<string, string>());

    private sealed class StaticConnector(ConnectorTarget target) : IConnector
    {
        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConnectorTarget>>([target]);
    }

    private sealed class BlockingConnector(Task<IReadOnlyList<ConnectorTarget>> task) : IConnector
    {
        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken) => task;
    }

    private sealed class MutableConnector(ConnectorTarget target) : IConnector
    {
        public ConnectorTarget Target { get; set; } = target;
        public int DiscoveryCount { get; private set; }

        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
        {
            DiscoveryCount++;
            return Task.FromResult<IReadOnlyList<ConnectorTarget>>([Target]);
        }
    }
}
