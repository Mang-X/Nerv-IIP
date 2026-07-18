using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed record ConnectorTagManifestReport(
    string OrganizationId,
    string EnvironmentId,
    string CollectionConnectorId,
    string SourceSystem,
    string ManifestRevision,
    DateTimeOffset ManifestObservedAtUtc,
    IReadOnlyList<ConnectorTagManifestEntrySnapshot> Entries);

public sealed record ConnectorTagManifestAcknowledgement(
    string Disposition,
    string AcceptedManifestRevision,
    DateTimeOffset AcceptedManifestObservedAtUtc);

public interface IConnectorTagManifestClient
{
    Task<ConnectorTagManifestAcknowledgement> ReportAsync(
        ConnectorTagManifestReport report,
        CancellationToken cancellationToken);
}

public sealed class ConnectorManifestReportingLoop
{
    private readonly IReadOnlyList<IConnector>? _connectors;
    private readonly ConnectorTargetSnapshotStore? _snapshotStore;
    private readonly ConnectorManifestReporter _manifestReporter;

    public ConnectorManifestReportingLoop(IReadOnlyList<IConnector> connectors, ConnectorManifestReporter manifestReporter)
    {
        _connectors = connectors;
        _manifestReporter = manifestReporter;
    }

    public ConnectorManifestReportingLoop(ConnectorTargetSnapshotStore snapshotStore, ConnectorManifestReporter manifestReporter)
    {
        _snapshotStore = snapshotStore;
        _manifestReporter = manifestReporter;
    }

    public async Task<DateTimeOffset?> RunCycleAsync(
        CancellationToken cancellationToken,
        string? forceRebirthConnectorId = null)
    {
        DateTimeOffset? nextAttemptAtUtc = null;
        var forceRebirthPending = !string.IsNullOrWhiteSpace(forceRebirthConnectorId);
        IReadOnlyList<ConnectorTarget> targets;
        if (_snapshotStore is not null)
        {
            _snapshotStore.TriggerRefresh(cancellationToken);
            targets = _snapshotStore.GetCurrentTargets();
        }
        else
        {
            var discovered = await Task.WhenAll(_connectors!.Select(connector => connector.DiscoverAsync(cancellationToken)));
            targets = discovered.SelectMany(static connectorTargets => connectorTargets).ToArray();
        }

        foreach (var manifest in targets
                     .Select(target => target.TagManifest)
                     .Where(static manifest => manifest is not null))
        {
                var forceRebirth = forceRebirthPending
                    && string.Equals(manifest!.CollectionConnectorId, forceRebirthConnectorId, StringComparison.Ordinal);
                var connectorNextAttemptAtUtc = await _manifestReporter.ReportAsync(
                    manifest!,
                    forceRebirth,
                    cancellationToken);
                forceRebirthPending &= !forceRebirth;
                if (connectorNextAttemptAtUtc.HasValue
                    && (!nextAttemptAtUtc.HasValue || connectorNextAttemptAtUtc < nextAttemptAtUtc))
                {
                    nextAttemptAtUtc = connectorNextAttemptAtUtc;
                }
        }

        return nextAttemptAtUtc;
    }
}

public sealed class ConnectorManifestReporter(
    IConnectorTagManifestClient client,
    ConnectorHostRuntimeContext runtimeContext,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, ManifestState> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DateTimeOffset?> ReportAsync(
        ConnectorTagManifestSnapshot snapshot,
        bool forceRebirth = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var state = GetState(snapshot.CollectionConnectorId);
            Observe(state, snapshot, forceRebirth, now);
            if (state.Pending is null || state.NextAttemptAtUtc > now)
            {
                return state.Pending is null ? null : state.NextAttemptAtUtc;
            }

            var attempted = state.Pending;
            try
            {
                var acknowledgement = await client.ReportAsync(attempted, cancellationToken);
                ApplyAcknowledgement(state, attempted, acknowledgement, timeProvider.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                state.FailedAttempts++;
                state.NextAttemptAtUtc = timeProvider.GetUtcNow() + RetryDelay(state.FailedAttempts);
            }

            return state.Pending is null ? null : state.NextAttemptAtUtc;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ManifestState GetState(string collectionConnectorId)
    {
        var normalized = Required(collectionConnectorId, nameof(collectionConnectorId));
        if (!_states.TryGetValue(normalized, out var state))
        {
            state = new ManifestState();
            _states[normalized] = state;
        }

        return state;
    }

    private void Observe(
        ManifestState state,
        ConnectorTagManifestSnapshot snapshot,
        bool forceRebirth,
        DateTimeOffset now)
    {
        var normalizedConnectorId = Required(snapshot.CollectionConnectorId, nameof(snapshot.CollectionConnectorId));
        var normalizedSourceSystem = Required(snapshot.SourceSystem, nameof(snapshot.SourceSystem)).ToLowerInvariant();
        var normalizedEntries = snapshot.Entries
            .Select(ConnectorManifestHasher.Normalize)
            .OrderBy(entry => entry.DeviceAssetId, StringComparer.Ordinal)
            .ThenBy(entry => entry.TagKey, StringComparer.Ordinal)
            .ToArray();
        var revision = ConnectorManifestHasher.Compute(normalizedSourceSystem, normalizedEntries);
        var configurationChanged = state.CurrentRevision is null
            || !string.Equals(state.CurrentRevision, revision, StringComparison.Ordinal)
            || !string.Equals(state.CurrentSourceSystem, normalizedSourceSystem, StringComparison.Ordinal);
        var rootObservationAdvanced = configurationChanged || forceRebirth;
        if (rootObservationAdvanced)
        {
            state.CurrentRevision = revision;
            state.CurrentSourceSystem = normalizedSourceSystem;
            state.CurrentManifestObservedAtUtc = NextObservation(state, now);
        }

        var candidate = new ConnectorTagManifestReport(
            runtimeContext.OrganizationId,
            runtimeContext.EnvironmentId,
            normalizedConnectorId,
            normalizedSourceSystem,
            revision,
            state.CurrentManifestObservedAtUtc,
            normalizedEntries);
        if (state.Pending is not null)
        {
            if (rootObservationAdvanced)
            {
                state.Pending = candidate;
                state.FailedAttempts = 0;
                state.NextAttemptAtUtc = now;
            }
            else
            {
                var mergedCandidate = MergeNewerActivationEntries(candidate, state.Pending);
                if (!mergedCandidate.Entries.SequenceEqual(state.Pending.Entries))
                {
                    state.Pending = mergedCandidate;
                    state.FailedAttempts = 0;
                    state.NextAttemptAtUtc = now;
                }
            }

            return;
        }

        if (state.LastAcknowledged is null || !SamePayload(candidate, state.LastAcknowledged))
        {
            state.Pending = candidate;
            state.FailedAttempts = 0;
            state.NextAttemptAtUtc = now;
        }
    }

    private static void ApplyAcknowledgement(
        ManifestState state,
        ConnectorTagManifestReport attempted,
        ConnectorTagManifestAcknowledgement acknowledgement,
        DateTimeOffset now)
    {
        var disposition = acknowledgement.Disposition.Trim().ToLowerInvariant();
        if (disposition is "accepted" or "idempotent")
        {
            state.LastAcknowledged = attempted;
            state.Pending = null;
            state.FailedAttempts = 0;
            return;
        }

        if (disposition is "stale" or "conflict")
        {
            var minimum = acknowledgement.AcceptedManifestObservedAtUtc.ToUniversalTime().AddTicks(1);
            var observation = NextObservation(state, now, minimum);
            state.CurrentManifestObservedAtUtc = observation;
            state.Pending = attempted with { ManifestObservedAtUtc = observation };
            state.FailedAttempts = 0;
            state.NextAttemptAtUtc = now;
            return;
        }

        state.FailedAttempts++;
        state.NextAttemptAtUtc = now + RetryDelay(state.FailedAttempts);
    }

    private static DateTimeOffset NextObservation(
        ManifestState state,
        DateTimeOffset now,
        DateTimeOffset? minimum = null)
    {
        var next = now.ToUniversalTime();
        if (state.LastAttemptedObservationAtUtc != default && next <= state.LastAttemptedObservationAtUtc)
        {
            next = state.LastAttemptedObservationAtUtc.AddTicks(1);
        }

        if (minimum.HasValue && next < minimum.Value)
        {
            next = minimum.Value;
        }

        state.LastAttemptedObservationAtUtc = next;
        return next;
    }

    private static ConnectorTagManifestReport MergeNewerActivationEntries(
        ConnectorTagManifestReport candidate,
        ConnectorTagManifestReport pending)
    {
        var pendingByKey = pending.Entries.ToDictionary(
            entry => (entry.DeviceAssetId, entry.TagKey));
        var merged = candidate.Entries
            .Select(entry => pendingByKey.TryGetValue((entry.DeviceAssetId, entry.TagKey), out var previous)
                             && previous.ActivationObservedAtUtc >= entry.ActivationObservedAtUtc
                ? previous
                : entry)
            .ToArray();
        return candidate with { Entries = merged };
    }

    private static bool SamePayload(ConnectorTagManifestReport left, ConnectorTagManifestReport right)
    {
        return left.OrganizationId == right.OrganizationId
            && left.EnvironmentId == right.EnvironmentId
            && left.CollectionConnectorId == right.CollectionConnectorId
            && left.SourceSystem == right.SourceSystem
            && left.ManifestRevision == right.ManifestRevision
            && left.ManifestObservedAtUtc == right.ManifestObservedAtUtc
            && left.Entries.SequenceEqual(right.Entries);
    }

    private static TimeSpan RetryDelay(int failedAttempts)
    {
        var seconds = Math.Pow(2, Math.Max(0, failedAttempts - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds));
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    private sealed class ManifestState
    {
        public string? CurrentRevision { get; set; }
        public string? CurrentSourceSystem { get; set; }
        public DateTimeOffset CurrentManifestObservedAtUtc { get; set; }
        public DateTimeOffset LastAttemptedObservationAtUtc { get; set; }
        public ConnectorTagManifestReport? Pending { get; set; }
        public ConnectorTagManifestReport? LastAcknowledged { get; set; }
        public int FailedAttempts { get; set; }
        public DateTimeOffset NextAttemptAtUtc { get; set; }
    }
}
