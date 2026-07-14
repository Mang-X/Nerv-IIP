using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Sdk.ConnectorProtocol;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed record ConnectorHostRuntimeContext(
    string ProtocolVersion,
    string SdkVersion,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    DateTimeOffset StartedAtUtc)
{
    public static ConnectorHostRuntimeContext DefaultLocal { get; } = new("1.0", "1.0", "org-001", "env-dev", "connector-host-001", DateTimeOffset.UtcNow);
}

public sealed class ConnectorReportingLoop(
    IReadOnlyList<IConnector> connectors,
    IConnectorProtocolClient connectorProtocolClient,
    ConnectorHostRuntimeContext runtimeContext)
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        foreach (var connector in connectors)
        {
            var targets = await connector.DiscoverAsync(cancellationToken);
            foreach (var target in targets)
            {
                var context = CreateContext();
                await connectorProtocolClient.SendRegistrationAsync(ToRegistration(context, target), cancellationToken);
                await connectorProtocolClient.SendHeartbeatAsync(ToHeartbeat(context, target), cancellationToken);
                await connectorProtocolClient.SendStateSnapshotAsync(ToStateSnapshot(context, target), cancellationToken);
            }
        }
    }

    private ConnectorRequestContext CreateContext()
    {
        return new ConnectorRequestContext(runtimeContext.ProtocolVersion, runtimeContext.SdkVersion, Guid.NewGuid().ToString("n"), DateTimeOffset.UtcNow, runtimeContext.OrganizationId, runtimeContext.EnvironmentId, runtimeContext.ConnectorHostId);
    }

    private static ApplicationRegistration ToRegistration(ConnectorRequestContext context, ConnectorTarget target)
    {
        return new ApplicationRegistration(
            context,
            $"{target.InstanceKey}:{target.Version}",
            target.NodeKey,
            target.NodeName,
            target.DeploymentKind,
            target.ApplicationKey,
            target.ApplicationName,
            target.Version,
            target.InstanceKey,
            target.InstanceName,
            target.Capabilities.Select(x => new CapabilityDescriptor(x.Code, x.Version, x.Category, x.SupportedOperations, new Dictionary<string, string>())).ToList(),
            target.Metadata);
    }

    private ApplicationHeartbeat ToHeartbeat(ConnectorRequestContext context, ConnectorTarget target)
    {
        return new ApplicationHeartbeat(context, target.InstanceKey, DateTimeOffset.UtcNow, target.HealthStatus == "healthy", runtimeContext.StartedAtUtc, 0, new Dictionary<string, string>());
    }

    private static InstanceStateSnapshot ToStateSnapshot(ConnectorRequestContext context, ConnectorTarget target)
    {
        var reportedAtUtc = DateTimeOffset.UtcNow;
        var health = target.CollectionHealth is null ? null : new ConnectorCollectionHealth(
            target.CollectionHealth.ConnectorId,
            target.CollectionHealth.SourceSystem,
            target.CollectionHealth.CounterEpoch,
            reportedAtUtc,
            target.CollectionHealth.ReceivedCount,
            target.CollectionHealth.DroppedCount,
            target.CollectionHealth.ErrorCount,
            target.CollectionHealth.LastSampleAtUtc);
        return new InstanceStateSnapshot(context, target.InstanceKey, reportedAtUtc, target.ReportedStatus, target.HealthStatus, $"{target.InstanceName} is {target.ReportedStatus}", new Dictionary<string, string>(), new Dictionary<string, decimal>(), target.Metadata, health);
    }
}
