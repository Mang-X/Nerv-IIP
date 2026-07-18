using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ConnectorManifestSignalTests
{
    [Fact]
    public async Task Activation_followed_by_rebirth_coalesces_to_one_forced_event()
    {
        var clock = new ControllableTimeProvider();
        var signal = new ConnectorManifestSignal();

        signal.Signal(" connector-a ");
        ((IConnectorManifestRebirthRequest)signal).RequestRebirth("connector-a");

        var pending = await signal.WaitAsync(TimeSpan.FromSeconds(1), clock, CancellationToken.None);
        Assert.Equal(new ConnectorManifestSignalEvent("connector-a", ForceRebirth: true), pending);
        await AssertNoPendingEventAsync(signal, clock);
    }

    [Fact]
    public async Task Rebirth_followed_by_activation_remains_one_forced_event()
    {
        var clock = new ControllableTimeProvider();
        var signal = new ConnectorManifestSignal();

        ((IConnectorManifestRebirthRequest)signal).RequestRebirth("connector-a");
        signal.Signal("connector-a");

        var pending = await signal.WaitAsync(TimeSpan.FromSeconds(1), clock, CancellationToken.None);
        Assert.Equal(new ConnectorManifestSignalEvent("connector-a", ForceRebirth: true), pending);
        await AssertNoPendingEventAsync(signal, clock);
    }

    [Fact]
    public async Task Different_connectors_are_each_consumed_and_rebirth_is_not_lost()
    {
        var signal = new ConnectorManifestSignal();

        signal.Signal("connector-a");
        ((IConnectorManifestRebirthRequest)signal).RequestRebirth("connector-b");
        ((IConnectorManifestRebirthRequest)signal).RequestRebirth("connector-a");

        var first = await signal.WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, CancellationToken.None);
        var second = await signal.WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, CancellationToken.None);

        Assert.Equal(new ConnectorManifestSignalEvent("connector-a", ForceRebirth: true), first);
        Assert.Equal(new ConnectorManifestSignalEvent("connector-b", ForceRebirth: true), second);
    }

    [Fact]
    public async Task Empty_wait_times_out_and_cancellation_is_propagated()
    {
        var clock = new ControllableTimeProvider();
        var signal = new ConnectorManifestSignal();
        var timeout = signal.WaitAsync(TimeSpan.FromSeconds(1), clock, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(await timeout);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => signal.WaitAsync(TimeSpan.FromSeconds(1), clock, cancellation.Token));
    }

    private static async Task AssertNoPendingEventAsync(
        ConnectorManifestSignal signal,
        ControllableTimeProvider clock)
    {
        var wait = signal.WaitAsync(TimeSpan.FromSeconds(1), clock, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await wait);
    }
}
