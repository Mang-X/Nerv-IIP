using System.Collections.Concurrent;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed class ConnectorTargetSnapshotStore(
    IReadOnlyList<IConnector> connectors,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<IConnector, Entry> _entries =
        new(connectors.Select(connector => KeyValuePair.Create(connector, new Entry())));

    public void Add(IConnector connector) => _entries.TryAdd(connector, new Entry());

    public async Task RemoveAsync(IConnector connector)
    {
        if (_entries.TryRemove(connector, out var entry))
        {
            Task? refresh;
            lock (entry.Gate)
            {
                entry.Removed = true;
                entry.RefreshRequested = false;
                refresh = entry.RefreshTask;
            }

            if (refresh is not null)
            {
                await refresh;
            }
        }
    }

    public void TriggerRefresh(CancellationToken cancellationToken, string? forceConnectorId = null)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (connector, entry) in _entries)
        {
            lock (entry.Gate)
            {
                if (entry.Removed)
                {
                    continue;
                }

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
        public bool Removed { get; set; }
        public bool RefreshRequested { get; set; }
    }
}
