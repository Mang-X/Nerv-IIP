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

    public static string DeriveStatus(InstanceHeartbeat? heartbeat, ConnectorCollectionHealthProjection? health, DateTimeOffset now)
    {
        if (health is null)
        {
            return "unknown";
        }

        var heartbeatStale = heartbeat is null
            || !heartbeat.Reachable
            || heartbeat.LastHeartbeatAtUtc.Add(StaleAfter) <= now;
        var metricsStale = health.ReportedAtUtc.Add(StaleAfter) <= now;
        return heartbeatStale || metricsStale ? "stale" : "current";
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
        return new ConnectorCollectionHealthListItem(
            instance.InstanceKey,
            instance.InstanceName,
            DeriveStatus(instance.Heartbeat, health, now),
            instance.Heartbeat?.LastHeartbeatAtUtc,
            health?.ReportedAtUtc,
            health?.LastSampleAtUtc,
            health?.ReceivedCount,
            health?.DroppedCount,
            health?.ErrorCount,
            health?.SourceSystem);
    }
}
