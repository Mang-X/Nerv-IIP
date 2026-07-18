using System.Collections.Concurrent;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed class ConnectorTargetSnapshotStore(
    IReadOnlyList<IConnector> connectors,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<IConnector, Entry> _entries = new();

    public void TriggerRefresh(CancellationToken cancellationToken, string? forceConnectorId = null)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var connector in connectors)
        {
            var entry = _entries.GetOrAdd(connector, static _ => new Entry());
            lock (entry.Gate)
            {
                var forceRefresh = forceConnectorId is not null
                    && entry.Targets.Any(target =>
                        string.Equals(target.InstanceKey, forceConnectorId, StringComparison.Ordinal)
                        || string.Equals(target.CollectionHealth?.ConnectorId, forceConnectorId, StringComparison.Ordinal));
                entry.RefreshRequested |= forceRefresh;
                if (entry.RefreshTask is { IsCompleted: false }
                    || !entry.RefreshRequested
                    && entry.LastSuccessfulRefreshAtUtc.HasValue
                    && now - entry.LastSuccessfulRefreshAtUtc.Value < RefreshInterval)
                {
                    continue;
                }

                entry.RefreshRequested = false;
                entry.RefreshTask = RefreshAsync(connector, entry, cancellationToken);
            }
        }
    }

    public IReadOnlyList<ConnectorTarget> GetCurrentTargets() =>
        _entries.Values
            .SelectMany(static entry => entry.Targets)
            .ToArray();

    private async Task RefreshAsync(IConnector connector, Entry entry, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var targets = await connector.DiscoverAsync(cancellationToken);
                entry.Targets = targets.ToArray();
                entry.LastSuccessfulRefreshAtUtc = timeProvider.GetUtcNow();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Preserve the last known snapshot. The connector's own health reporting
                // carries discovery failures without blocking unrelated connectors.
            }

            lock (entry.Gate)
            {
                if (!entry.RefreshRequested)
                {
                    return;
                }

                entry.RefreshRequested = false;
            }
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public IReadOnlyList<ConnectorTarget> Targets { get; set; } = [];
        public DateTimeOffset? LastSuccessfulRefreshAtUtc { get; set; }
        public Task? RefreshTask { get; set; }
        public bool RefreshRequested { get; set; }
    }
}
