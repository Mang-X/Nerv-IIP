namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public sealed class ConnectorConnectionStateTracker
{
    private readonly object _gate = new();
    private readonly string _connectorId;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _signal;
    private ConnectorConnectionStateSnapshot _snapshot;

    public ConnectorConnectionStateTracker(string connectorId, TimeProvider timeProvider, Action<string> signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(signal);

        _connectorId = connectorId;
        _timeProvider = timeProvider;
        _signal = signal;
        _snapshot = new ConnectorConnectionStateSnapshot("unknown", timeProvider.GetUtcNow());
    }

    public ConnectorConnectionStateSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void MarkAlive()
    {
        lock (_gate)
        {
            if (_snapshot.Status == "alive")
            {
                return;
            }

            var observedAtUtc = GetMonotonicUtcNow();
            _snapshot = new ConnectorConnectionStateSnapshot(
                "alive",
                observedAtUtc,
                ConnectedSinceUtc: observedAtUtc);
        }

        _signal(_connectorId);
    }

    public void MarkLost(string reasonCategory, string diagnosticCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCategory);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);

        lock (_gate)
        {
            if (_snapshot.Status == "lost"
                && _snapshot.ReasonCategory == reasonCategory
                && _snapshot.DiagnosticCode == diagnosticCode)
            {
                return;
            }

            var observedAtUtc = GetMonotonicUtcNow();
            var disconnectedSinceUtc = _snapshot.Status == "lost"
                ? _snapshot.DisconnectedSinceUtc
                : observedAtUtc;
            _snapshot = new ConnectorConnectionStateSnapshot(
                "lost",
                observedAtUtc,
                DisconnectedSinceUtc: disconnectedSinceUtc,
                ReasonCategory: reasonCategory,
                DiagnosticCode: diagnosticCode);
        }

        _signal(_connectorId);
    }

    private DateTimeOffset GetMonotonicUtcNow()
    {
        var utcNow = _timeProvider.GetUtcNow();
        return utcNow <= _snapshot.ObservedAtUtc ? _snapshot.ObservedAtUtc.AddTicks(1) : utcNow;
    }
}
