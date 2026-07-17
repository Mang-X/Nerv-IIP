using System.Threading.Channels;

namespace Nerv.IIP.ConnectorHost.Application;

public interface IConnectorReportSignal
{
    void Signal(string connectorId);

    Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken);
}

public sealed class ConnectorReportSignal : IConnectorReportSignal
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Signal(string connectorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        _channel.Writer.TryWrite(connectorId);
    }

    public async Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Report wait timeout must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(timeProvider);

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _channel.Reader.ReadAsync(waitCancellation.Token).AsTask();
        var timeoutTask = Task.Delay(timeout, timeProvider, waitCancellation.Token);
        var completedTask = await Task.WhenAny(signalTask, timeoutTask);
        await completedTask;
        await waitCancellation.CancelAsync();
    }
}
