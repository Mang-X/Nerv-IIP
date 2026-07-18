using System.Collections.Concurrent;
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

public sealed class ConnectorReportingLoop
{
    private static readonly TimeSpan RegistrationRefreshInterval = TimeSpan.FromMinutes(5);
    private readonly IReadOnlyList<IConnector>? _connectors;
    private readonly ConnectorTargetSnapshotStore? _snapshotStore;
    private readonly IConnectorProtocolClient _connectorProtocolClient;
    private readonly ConnectorHostRuntimeContext _runtimeContext;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, RegistrationState> _registrations = new(StringComparer.Ordinal);

    public ConnectorReportingLoop(
        IReadOnlyList<IConnector> connectors,
        IConnectorProtocolClient connectorProtocolClient,
        ConnectorHostRuntimeContext runtimeContext)
        : this(connectors, null, connectorProtocolClient, runtimeContext, TimeProvider.System)
    {
    }

    public ConnectorReportingLoop(
        ConnectorTargetSnapshotStore snapshotStore,
        IConnectorProtocolClient connectorProtocolClient,
        ConnectorHostRuntimeContext runtimeContext,
        TimeProvider timeProvider)
        : this(null, snapshotStore, connectorProtocolClient, runtimeContext, timeProvider)
    {
    }

    private ConnectorReportingLoop(
        IReadOnlyList<IConnector>? connectors,
        ConnectorTargetSnapshotStore? snapshotStore,
        IConnectorProtocolClient connectorProtocolClient,
        ConnectorHostRuntimeContext runtimeContext,
        TimeProvider timeProvider)
    {
        _connectors = connectors;
        _snapshotStore = snapshotStore;
        _connectorProtocolClient = connectorProtocolClient;
        _runtimeContext = runtimeContext;
        _timeProvider = timeProvider;
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken, string? changedConnectorId = null)
    {
        IReadOnlyList<ConnectorTarget> targets;
        if (_snapshotStore is not null)
        {
            _snapshotStore.TriggerRefresh(cancellationToken, changedConnectorId);
            targets = _snapshotStore.GetCurrentTargets();
        }
        else
        {
            var discovered = await Task.WhenAll(_connectors!.Select(connector => connector.DiscoverAsync(cancellationToken)));
            targets = discovered.SelectMany(static connectorTargets => connectorTargets).ToArray();
        }

        await Task.WhenAll(targets.Select(target => ReportTargetAsync(target, cancellationToken)));
    }

    private async Task ReportTargetAsync(ConnectorTarget target, CancellationToken cancellationToken)
    {
        var context = CreateContext();
        var now = _timeProvider.GetUtcNow();
        var fingerprint = RegistrationFingerprint(target);
        if (!_registrations.TryGetValue(target.InstanceKey, out var registration)
            || registration.Fingerprint != fingerprint
            || now - registration.RegisteredAtUtc >= RegistrationRefreshInterval)
        {
            await _connectorProtocolClient.SendRegistrationAsync(ToRegistration(context, target), cancellationToken);
            _registrations[target.InstanceKey] = new RegistrationState(fingerprint, now);
        }

        try
        {
            await _connectorProtocolClient.SendHeartbeatAsync(ToHeartbeat(context, target), cancellationToken);
            await _connectorProtocolClient.SendStateSnapshotAsync(ToStateSnapshot(context, target), cancellationToken);
        }
        catch
        {
            _registrations.TryRemove(target.InstanceKey, out _);
            throw;
        }
    }

    private ConnectorRequestContext CreateContext()
    {
        return new ConnectorRequestContext(_runtimeContext.ProtocolVersion, _runtimeContext.SdkVersion, Guid.NewGuid().ToString("n"), _timeProvider.GetUtcNow(), _runtimeContext.OrganizationId, _runtimeContext.EnvironmentId, _runtimeContext.ConnectorHostId);
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
        return new ApplicationHeartbeat(context, target.InstanceKey, _timeProvider.GetUtcNow(), true, _runtimeContext.StartedAtUtc, 0, new Dictionary<string, string>());
    }

    private static InstanceStateSnapshot ToStateSnapshot(ConnectorRequestContext context, ConnectorTarget target)
    {
        var reportedAtUtc = context.OccurredAtUtc;
        var health = target.CollectionHealth is null ? null : new ConnectorCollectionHealth(
            target.CollectionHealth.ConnectorId,
            target.CollectionHealth.SourceSystem,
            target.CollectionHealth.CounterEpoch,
            reportedAtUtc,
            target.CollectionHealth.ReceivedCount,
            target.CollectionHealth.DroppedCount,
            target.CollectionHealth.ErrorCount,
            target.CollectionHealth.LastSampleAtUtc,
            target.CollectionHealth.Connection is null ? null : new ConnectorConnectionState(
                target.CollectionHealth.Connection.Status,
                target.CollectionHealth.Connection.ObservedAtUtc,
                target.CollectionHealth.Connection.ConnectedSinceUtc,
                target.CollectionHealth.Connection.DisconnectedSinceUtc,
                target.CollectionHealth.Connection.ReasonCategory,
                target.CollectionHealth.Connection.DiagnosticCode));
        return new InstanceStateSnapshot(context, target.InstanceKey, reportedAtUtc, target.ReportedStatus, target.HealthStatus, $"{target.InstanceName} is {target.ReportedStatus}", new Dictionary<string, string>(), new Dictionary<string, decimal>(), target.Metadata, health);
    }

    private static string RegistrationFingerprint(ConnectorTarget target) => string.Join(
        '\u001f',
        target.NodeKey,
        target.DeploymentKind,
        target.ApplicationKey,
        target.Version,
        target.InstanceKey,
        string.Join(',', target.Capabilities.Select(static capability => $"{capability.Code}:{capability.Version}")));

    private sealed record RegistrationState(string Fingerprint, DateTimeOffset RegisteredAtUtc);
}
