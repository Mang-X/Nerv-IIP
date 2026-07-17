namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public interface IConnector
{
    Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IIndustrialTelemetryCollectionConnector
{
    Task RunCollectionCycleAsync(CancellationToken cancellationToken);
}

public interface IConnectorConnectionMonitor
{
    Task RunConnectionCheckAsync(CancellationToken cancellationToken);
}

public sealed record ConnectorCollectionHealthSnapshot(
    string ConnectorId,
    string SourceSystem,
    Guid CounterEpoch,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    DateTimeOffset? LastSampleAtUtc,
    ConnectorConnectionStateSnapshot? Connection = null);

public sealed record ConnectorConnectionStateSnapshot(
    string Status,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ConnectedSinceUtc = null,
    DateTimeOffset? DisconnectedSinceUtc = null,
    string? ReasonCategory = null,
    string? DiagnosticCode = null);

public sealed record ConnectorTagManifestSnapshot(
    string CollectionConnectorId,
    string SourceSystem,
    IReadOnlyList<ConnectorTagManifestEntrySnapshot> Entries);

public sealed record ConnectorTagManifestEntrySnapshot(
    string DeviceAssetId,
    string TagKey,
    bool Enabled,
    string? ProtocolAddress,
    string ActivationStatus,
    DateTimeOffset ActivationObservedAtUtc,
    string? ActivationErrorCode = null,
    string? ActivationErrorMessage = null);

public sealed record ConnectorTarget(
    string NodeKey,
    string NodeName,
    string DeploymentKind,
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string InstanceKey,
    string InstanceName,
    string ReportedStatus,
    string HealthStatus,
    IReadOnlyList<ConnectorCapability> Capabilities,
    IReadOnlyDictionary<string, string> Metadata,
    ConnectorCollectionHealthSnapshot? CollectionHealth = null,
    ConnectorTagManifestSnapshot? TagManifest = null);

public sealed record ConnectorCapability(
    string Code,
    string Version,
    string Category,
    IReadOnlyList<string> SupportedOperations);

public sealed record ConnectorStateSnapshot(
    string InstanceKey,
    string ReportedStatus,
    string HealthStatus,
    string Summary,
    IReadOnlyDictionary<string, string> Detail,
    IReadOnlyDictionary<string, decimal> Metrics);
