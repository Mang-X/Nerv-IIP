namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public sealed class ConnectorTagManifestTracker
{
    private readonly object _gate = new();
    private readonly string _collectionConnectorId;
    private readonly string _sourceSystem;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _signal;
    private readonly Dictionary<(string DeviceAssetId, string TagKey), ConnectorTagManifestEntrySnapshot> _entries;

    public ConnectorTagManifestTracker(
        string collectionConnectorId,
        string sourceSystem,
        IReadOnlyCollection<ConnectorTagManifestDefinition> definitions,
        TimeProvider timeProvider,
        Action<string> signal)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _collectionConnectorId = Required(collectionConnectorId, nameof(collectionConnectorId));
        _sourceSystem = Required(sourceSystem, nameof(sourceSystem)).ToLowerInvariant();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        var observedAtUtc = _timeProvider.GetUtcNow();
        _entries = definitions.ToDictionary(
            definition => (Required(definition.DeviceAssetId, nameof(definition.DeviceAssetId)), Required(definition.TagKey, nameof(definition.TagKey)).ToLowerInvariant()),
            definition => new ConnectorTagManifestEntrySnapshot(
                Required(definition.DeviceAssetId, nameof(definition.DeviceAssetId)),
                Required(definition.TagKey, nameof(definition.TagKey)).ToLowerInvariant(),
                definition.Enabled,
                definition.ProtocolAddress,
                definition.Enabled ? "pending" : "disabled",
                observedAtUtc));
    }

    public ConnectorTagManifestSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new ConnectorTagManifestSnapshot(
                    _collectionConnectorId,
                    _sourceSystem,
                    _entries.Values
                        .OrderBy(entry => entry.DeviceAssetId, StringComparer.Ordinal)
                        .ThenBy(entry => entry.TagKey, StringComparer.Ordinal)
                        .ToArray());
            }
        }
    }

    public void MarkActive(string deviceAssetId, string tagKey) =>
        Update(deviceAssetId, tagKey, "active", null, null);

    public void MarkError(string deviceAssetId, string tagKey, string errorCode, string errorMessage) =>
        Update(deviceAssetId, tagKey, "error", errorCode, errorMessage);

    public void MarkAllEnabledActive() => UpdateAllEnabled("active", null, null);

    public void MarkAllEnabledError(string errorCode, string errorMessage) =>
        UpdateAllEnabled("error", errorCode, errorMessage);

    private void UpdateAllEnabled(string status, string? errorCode, string? errorMessage)
    {
        (string DeviceAssetId, string TagKey)[] keys;
        lock (_gate)
        {
            keys = _entries.Values
                .Where(entry => entry.Enabled)
                .Select(entry => (entry.DeviceAssetId, entry.TagKey))
                .ToArray();
        }

        foreach (var key in keys)
        {
            Update(key.DeviceAssetId, key.TagKey, status, errorCode, errorMessage);
        }
    }

    private void Update(string deviceAssetId, string tagKey, string status, string? errorCode, string? errorMessage)
    {
        var key = (Required(deviceAssetId, nameof(deviceAssetId)), Required(tagKey, nameof(tagKey)).ToLowerInvariant());
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current) || !current.Enabled)
            {
                return;
            }

            if (current.ActivationStatus == status
                && current.ActivationErrorCode == errorCode
                && current.ActivationErrorMessage == errorMessage)
            {
                return;
            }

            var observedAtUtc = _timeProvider.GetUtcNow();
            if (observedAtUtc <= current.ActivationObservedAtUtc)
            {
                observedAtUtc = current.ActivationObservedAtUtc.AddTicks(1);
            }

            _entries[key] = current with
            {
                ActivationStatus = status,
                ActivationObservedAtUtc = observedAtUtc,
                ActivationErrorCode = status == "error" ? errorCode : null,
                ActivationErrorMessage = status == "error" ? errorMessage : null,
            };
        }

        _signal(_collectionConnectorId);
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();
}

public sealed record ConnectorTagManifestDefinition(
    string DeviceAssetId,
    string TagKey,
    bool Enabled,
    string? ProtocolAddress);
