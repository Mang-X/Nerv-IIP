namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public interface IConnector
{
    Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IIndustrialTelemetryCollectionConnector
{
    Task RunCollectionCycleAsync(CancellationToken cancellationToken);
}

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
    IReadOnlyDictionary<string, string> Metadata);

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
