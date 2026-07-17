using Microsoft.Extensions.Options;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Application.Connectors;

/// <summary>
/// Derives the single-connector and list read models from independent host-liveness, field-connection,
/// collector-health and sampling facts. Sample silence never implies a connection loss.
/// </summary>
public sealed class ConnectorCollectionHealthEvaluator(IOptions<ConnectorCollectionHealthOptions> options)
{
    /// <summary>The coarse stale reason for an unavailable host or field connection.</summary>
    public const string StaleReasonOffline = "offline";

    /// <summary>The coarse stale reason for a live host whose collector reports a terminal failure.</summary>
    public const string StaleReasonFault = "fault";

    public const string OfflineReasonFieldConnection = "field-connection";
    public const string OfflineReasonHostLiveness = "host-liveness";

    private readonly TimeSpan hostLivenessTimeout = options.Value.HostLivenessTimeout;

    public string DeriveStatus(
        InstanceHeartbeat? heartbeat,
        ConnectorCollectionHealthProjection? health,
        string reportedStatus,
        string healthStatus,
        DateTimeOffset now)
        => DeriveStatusAndReason(heartbeat, health, reportedStatus, healthStatus, now).Status;

    public (string Status, string? StaleReason, string? OfflineReason) DeriveStatusAndReason(
        InstanceHeartbeat? heartbeat,
        ConnectorCollectionHealthProjection? health,
        string reportedStatus,
        string healthStatus,
        DateTimeOffset now)
    {
        if (string.Equals(health?.ConnectionStatus, "lost", StringComparison.Ordinal))
        {
            return ("stale", StaleReasonOffline, OfflineReasonFieldConnection);
        }

        var heartbeatTimedOut = heartbeat is null
            || heartbeat.LastHeartbeatAtUtc.Add(hostLivenessTimeout) <= now;
        if (heartbeatTimedOut)
        {
            return ("stale", StaleReasonOffline, OfflineReasonHostLiveness);
        }

        if (health is null)
        {
            return ("unknown", null, null);
        }

        var reportedFault = string.Equals(reportedStatus, "stopped", StringComparison.OrdinalIgnoreCase)
            || string.Equals(healthStatus, "unhealthy", StringComparison.OrdinalIgnoreCase);
        if (reportedFault)
        {
            return ("stale", StaleReasonFault, null);
        }

        var noSamplingEvidence = health.ReceivedCount is null && health.LastSampleAtUtc is null;
        return noSamplingEvidence ? ("unknown", null, null) : ("current", null, null);
    }

    public ConnectorCollectionHealthResponse ToResponse(ApplicationInstance instance, DateTimeOffset now)
    {
        var health = instance.CollectionHealth;
        var (status, staleReason, offlineReason) = DeriveStatusAndReason(
            instance.Heartbeat,
            health,
            instance.ReportedStatus,
            instance.HealthStatus,
            now);
        return new ConnectorCollectionHealthResponse(
            instance.InstanceKey,
            status,
            instance.Heartbeat?.LastHeartbeatAtUtc,
            health?.ReportedAtUtc,
            health?.LastSampleAtUtc,
            health?.ReceivedCount,
            health?.DroppedCount,
            health?.ErrorCount,
            health?.SourceSystem,
            ToConnection(health),
            staleReason,
            offlineReason);
    }

    public ConnectorCollectionHealthListItem ToListItem(ApplicationInstance instance, DateTimeOffset now)
    {
        var health = instance.CollectionHealth;
        var (status, staleReason, offlineReason) = DeriveStatusAndReason(
            instance.Heartbeat,
            health,
            instance.ReportedStatus,
            instance.HealthStatus,
            now);
        return new ConnectorCollectionHealthListItem(
            instance.InstanceKey,
            instance.InstanceName,
            status,
            staleReason,
            instance.Heartbeat?.LastHeartbeatAtUtc,
            health?.ReportedAtUtc,
            health?.LastSampleAtUtc,
            health?.ReceivedCount,
            health?.DroppedCount,
            health?.ErrorCount,
            health?.CounterEpoch,
            health?.SourceSystem,
            ToConnection(health),
            offlineReason);
    }

    private static ConnectorConnectionState? ToConnection(ConnectorCollectionHealthProjection? health)
    {
        if (health?.ConnectionStatus is not { } status || health.ConnectionObservedAtUtc is not { } observedAtUtc)
        {
            return null;
        }

        return new ConnectorConnectionState(
            status,
            observedAtUtc,
            health.ConnectedSinceUtc,
            health.DisconnectedSinceUtc,
            health.ConnectionReasonCategory,
            health.ConnectionDiagnosticCode);
    }
}
