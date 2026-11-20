using System.Threading.Channels;

namespace Nerv.IIP.ConnectorHost.Application;

public interface IConnectorReportSignal
{
    void Signal(string connectorId);

    Task<string?> WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken);
}

public sealed class ConnectorReportSignal : IConnectorReportSignal
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingOrder = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Signal(string connectorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        var normalized = connectorId.Trim();
        lock (_gate)
        {
            if (_pending.Add(normalized))
            {
                _pendingOrder.Enqueue(normalized);
            }

            _wake.Writer.TryWrite(true);
        }
    }

    public Task<string?> WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        => ConnectorSignalWait.WaitAsync(_wake.Reader, TakePending, timeout, timeProvider, cancellationToken,
            "Report wait timeout must be greater than zero.");

    private string? TakePending()
    {
        lock (_gate)
        {
            if (_pendingOrder.Count == 0)
            {
                return null;
            }

            var connectorId = _pendingOrder.Dequeue();
            _pending.Remove(connectorId);
            _wake.Reader.TryRead(out _);
            if (_pendingOrder.Count > 0)
            {
                _wake.Writer.TryWrite(true);
            }

            return connectorId;
        }
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

    public Task<ConnectorManifestSignalEvent?> WaitAsync(
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
        => ConnectorSignalWait.WaitAsync(_wake.Reader, TakePending, timeout, timeProvider, cancellationToken,
            "Manifest wait timeout must be greater than zero.");

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

    private ConnectorManifestSignalEvent? TakePending()
    {
        lock (_gate)
        {
            if (_pendingOrder.Count == 0)
            {
                return null;
            }

            var connectorId = _pendingOrder.Dequeue();
            var forceRebirth = _pending.Remove(connectorId, out var pendingForceRebirth)
                && pendingForceRebirth;
            _wake.Reader.TryRead(out _);
            if (_pendingOrder.Count > 0)
            {
                _wake.Writer.TryWrite(true);
            }

            return new ConnectorManifestSignalEvent(connectorId, forceRebirth);
        }
    }
}

internal static class ConnectorSignalWait
{
    internal static async Task<T?> WaitAsync<T>(
        ChannelReader<bool> wake,
        Func<T?> takePending,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        string timeoutMessage) where T : class
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeoutMessage);
        }

        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();
        var pending = takePending();
        if (pending is not null)
        {
            return pending;
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = wake.ReadAsync(waitCancellation.Token).AsTask();
        var timeoutTask = Task.Delay(timeout, timeProvider, waitCancellation.Token);
        try
        {
            var completedTask = await Task.WhenAny(signalTask, timeoutTask);
            cancellationToken.ThrowIfCancellationRequested();
            await completedTask;
            return takePending();
        }
        finally
        {
            await waitCancellation.CancelAsync();
        }
    }
}
