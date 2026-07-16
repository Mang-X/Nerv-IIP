using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Application.Connectors;

/// <summary>
/// Shared derivation of a connector-host instance's collection-health status. Kept in one place so the
/// single-connector and list read endpoints report identical semantics.
/// <para>
/// The derivation deliberately does NOT use the heartbeat <c>Reachable</c> flag: upstream
/// <c>ConnectorReportingLoop.ToHeartbeat</c> sets it from <c>HealthStatus == "healthy"</c>, and a connector
/// that is actively collecting but has any historical drop/reconnect stays <c>degraded</c> (hence
/// <c>Reachable=false</c>) — so <c>!Reachable</c> would falsely brand a live collector as disconnected.
/// Instead a genuine disconnect (<see cref="StaleReasonHeartbeat"/>) is derived from non-conflated facts:
/// the heartbeat stopped arriving (aged out) or the connector reported a terminal down state
/// (<c>ReportedStatus == "stopped"</c> / <c>HealthStatus == "unhealthy"</c>). A connector that is still
/// heartbeating and running but whose sampling stopped advancing is <see cref="StaleReasonMetrics"/>
/// (online, collection stalled), not a disconnect.
/// </para>
/// </summary>
public static class ConnectorCollectionHealthEvaluator
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>heartbeat stopped arriving or the connector reported a terminal down state — a real disconnect (断线).</summary>
    public const string StaleReasonHeartbeat = "heartbeat";

    /// <summary>heartbeat is still fresh and the connector is running, but sampling stopped advancing — online, collection stalled (采集停滞).</summary>
    public const string StaleReasonMetrics = "metrics";

    public static string DeriveStatus(
        InstanceHeartbeat? heartbeat, ConnectorCollectionHealthProjection? health, string reportedStatus, string healthStatus, DateTimeOffset now)
        => DeriveStatusAndReason(heartbeat, health, reportedStatus, healthStatus, now).Status;

    /// <summary>
    /// Derives status and, when stale, why. A genuine disconnect takes precedence over a collection stall so
    /// the UI can label a real 断线 distinctly from an online-but-stalled collector.
    /// </summary>
    public static (string Status, string? StaleReason) DeriveStatusAndReason(
        InstanceHeartbeat? heartbeat,
        ConnectorCollectionHealthProjection? health,
        string reportedStatus,
        string healthStatus,
        DateTimeOffset now)
    {
        if (health is null)
        {
            return ("unknown", null);
        }

        var heartbeatMissing = heartbeat is null || heartbeat.LastHeartbeatAtUtc.Add(StaleAfter) <= now;
        var reportedDown = string.Equals(reportedStatus, "stopped", StringComparison.OrdinalIgnoreCase)
            || string.Equals(healthStatus, "unhealthy", StringComparison.OrdinalIgnoreCase);
        if (heartbeatMissing || reportedDown)
        {
            return ("stale", StaleReasonHeartbeat);
        }

        // Collection stall = the connector is still reporting/running but produced no new sample within the
        // window. Keyed on LastSampleAtUtc (actual collection activity), not ReportedAtUtc (which advances
        // every reporting cycle even when nothing is collected). Never-sampled connectors are not flagged.
        var lastSample = health.LastSampleAtUtc;
        var collectionStale = lastSample is { } sampledAt && sampledAt.Add(StaleAfter) <= now;
        return collectionStale ? ("stale", StaleReasonMetrics) : ("current", null);
    }

    public static ConnectorCollectionHealthResponse ToResponse(ApplicationInstance instance, DateTimeOffset now)
    {
        var health = instance.CollectionHealth;
        return new ConnectorCollectionHealthResponse(
            instance.InstanceKey,
            DeriveStatus(instance.Heartbeat, health, instance.ReportedStatus, instance.HealthStatus, now),
            instance.Heartbeat?.LastHeartbeatAtUtc,
            health?.ReportedAtUtc,
            health?.LastSampleAtUtc,
            health?.ReceivedCount,
            health?.DroppedCount,
            health?.ErrorCount,
            health?.SourceSystem);
    }

    public static ConnectorCollectionHealthListItem ToListItem(ApplicationInstance instance, DateTimeOffset now)
    {
        var health = instance.CollectionHealth;
        var (status, staleReason) = DeriveStatusAndReason(
            instance.Heartbeat, health, instance.ReportedStatus, instance.HealthStatus, now);
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
            health?.SourceSystem);
    }
}
