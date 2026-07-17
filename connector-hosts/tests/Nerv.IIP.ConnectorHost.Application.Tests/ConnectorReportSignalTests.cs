using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ConnectorReportSignalTests
{
    [Fact]
    public async Task Capacity_one_signal_coalesces_repeated_notifications()
    {
        var signal = new ConnectorReportSignal();
        var timeProvider = new ControllableTimeProvider();

        signal.Signal("connector-a");
        signal.Signal("connector-a");

        await signal.WaitAsync(TimeSpan.FromMinutes(1), timeProvider, CancellationToken.None);
        var secondWait = signal.WaitAsync(TimeSpan.FromMinutes(1), timeProvider, CancellationToken.None);

        Assert.False(secondWait.IsCompleted);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await secondWait;
    }

    [Fact]
    public async Task Wait_completes_when_timeout_elapses_without_a_signal()
    {
        var signal = new ConnectorReportSignal();
        var timeProvider = new ControllableTimeProvider();
        var wait = signal.WaitAsync(TimeSpan.FromSeconds(2), timeProvider, CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        await wait;
    }

}
