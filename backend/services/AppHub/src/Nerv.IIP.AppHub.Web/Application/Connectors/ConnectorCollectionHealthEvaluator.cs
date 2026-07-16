using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Application.Connectors;

/// <summary>
/// Shared derivation of a connector-host instance's collection-health status. A connector is
/// <c>current</c> only when a collection-health fact exists and both the heartbeat and metrics are fresh;
/// a heartbeat that is missing, unreachable, or older than <see cref="StaleAfter"/> (or stale metrics)
/// makes it <c>stale</c>; no collection-health fact at all is <c>unknown</c>. Kept in one place so the
/// single-connector and list read endpoints report identical semantics.
/// </summary>
public static class ConnectorCollectionHealthEvaluator
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>heartbeat is missing / unreachable / aged out — treat as a real disconnect (断线).</summary>
    public const string StaleReasonHeartbeat = "heartbeat";

    /// <summary>heartbeat is still fresh but collection metrics stopped advancing — still online, collection stalled (采集停滞).</summary>
    public const string StaleReasonMetrics = "metrics";

    public static string DeriveStatus(InstanceHeartbeat? heartbeat, ConnectorCollectionHealthProjection? health, DateTimeOffset now)
        => DeriveStatusAndReason(heartbeat, health, now).Status;

    /// <summary>
    /// Derives status and, when stale, why. Heartbeat loss takes precedence over metrics staleness so the
    /// UI can label a genuine disconnect distinctly from an online-but-stalled collector.
    /// </summary>
    public static (string Status, string? StaleReason) DeriveStatusAndReason(
        InstanceHeartbeat? heartbeat, ConnectorCollectionHealthProjection? health, DateTimeOffset now)
    {
        if (health is null)
        {
            return ("unknown", null);
        }

        var heartbeatStale = heartbeat is null
            || !heartbeat.Reachable
            || heartbeat.LastHeartbeatAtUtc.Add(StaleAfter) <= now;
        if (heartbeatStale)
        {
            return ("stale", StaleReasonHeartbeat);
        }

        var metricsStale = health.ReportedAtUtc.Add(StaleAfter) <= now;
        return metricsStale ? ("stale", StaleReasonMetrics) : ("current", null);
    }

    public static ConnectorCollectionHealthResponse ToResponse(ApplicationInstance instance, DateTimeOffset now)
    {
        var health = instance.CollectionHealth;
        return new ConnectorCollectionHealthResponse(
            instance.InstanceKey,
            DeriveStatus(instance.Heartbeat, health, now),
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
        var (status, staleReason) = DeriveStatusAndReason(instance.Heartbeat, health, now);
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
