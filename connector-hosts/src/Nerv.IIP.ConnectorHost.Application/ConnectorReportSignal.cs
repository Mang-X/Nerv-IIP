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

public interface IConnectorManifestSignal
{
    void Signal(string connectorId);

    Task<ConnectorManifestSignalEvent?> WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken);
}

public interface IConnectorManifestRebirthRequest
{
    void RequestRebirth(string connectorId);
}

public sealed record ConnectorManifestSignalEvent(string ConnectorId, bool ForceRebirth);

public sealed class ConnectorManifestSignal : IConnectorManifestSignal, IConnectorManifestRebirthRequest
{
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingOrder = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public void Signal(string connectorId) => Write(connectorId, forceRebirth: false);

    public void RequestRebirth(string connectorId) => Write(connectorId, forceRebirth: true);

    public async Task<ConnectorManifestSignalEvent?> WaitAsync(
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Manifest wait timeout must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryTakePending(out var pending))
        {
            return pending;
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _wake.Reader.ReadAsync(waitCancellation.Token).AsTask();
        var timeoutTask = Task.Delay(timeout, timeProvider, waitCancellation.Token);
        try
        {
            var completedTask = await Task.WhenAny(signalTask, timeoutTask);
            cancellationToken.ThrowIfCancellationRequested();
            await completedTask;
            return TryTakePending(out pending) ? pending : null;
        }
        finally
        {
            await waitCancellation.CancelAsync();
        }
    }

    private void Write(string connectorId, bool forceRebirth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        var normalized = connectorId.Trim();
        lock (_gate)
        {
            if (_pending.TryGetValue(normalized, out var existingForceRebirth))
            {
                _pending[normalized] = existingForceRebirth || forceRebirth;
            }
            else
            {
                _pending.Add(normalized, forceRebirth);
                _pendingOrder.Enqueue(normalized);
            }

            _wake.Writer.TryWrite(true);
        }
    }

    private bool TryTakePending(out ConnectorManifestSignalEvent? signal)
    {
        lock (_gate)
        {
            if (_pendingOrder.Count == 0)
            {
                signal = null;
                return false;
            }

            var connectorId = _pendingOrder.Dequeue();
            var forceRebirth = _pending.Remove(connectorId, out var pendingForceRebirth)
                && pendingForceRebirth;
            _wake.Reader.TryRead(out _);
            if (_pendingOrder.Count > 0)
            {
                _wake.Writer.TryWrite(true);
            }

            signal = new ConnectorManifestSignalEvent(connectorId, forceRebirth);
            return true;
        }
    }
}
