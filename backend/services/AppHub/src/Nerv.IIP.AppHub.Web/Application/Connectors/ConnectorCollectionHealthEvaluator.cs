using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Application.Connectors;

/// <summary>
/// Shared derivation of a connector-host instance's collection-health status. Kept in one place so the
/// single-connector and list read endpoints report identical semantics.
/// <para>
/// Only <b>unambiguous</b> facts drive the derived status; anything that cannot be told apart cleanly is
/// left to the raw throughput facts the caller also receives (received/dropped/error/last-sample), not to a
/// fabricated status:
/// </para>
/// <list type="bullet">
/// <item><see cref="StaleReasonOffline"/> (断线): the heartbeat stopped arriving (aged past
/// <see cref="StaleAfter"/>). The connector-host is no longer reporting to AppHub — a genuine disconnect.
/// Because heartbeats have stopped, <c>LastHeartbeatAtUtc</c> is frozen, so a "disconnected for" duration
/// derived from it increases monotonically.</item>
/// <item><see cref="StaleReasonFault"/> (异常停止): the connector is still heartbeating but self-reported a
/// terminal state (<c>ReportedStatus == "stopped"</c> / <c>HealthStatus == "unhealthy"</c>). The root cause
/// is not observable here (it may be a lost connection, but equally a downstream/processing/config failure),
/// so it is reported as an abnormal stop — never conflated with a device disconnect.</item>
/// </list>
/// <para>
/// The heartbeat <c>Reachable</c> flag is deliberately NOT used: upstream <c>ToHeartbeat</c> sets it from
/// <c>HealthStatus == "healthy"</c>, so a live collector with any historical drop/reconnect (permanently
/// <c>degraded</c>) would be branded disconnected. A collection stall is likewise NOT derived from
/// <c>LastSampleAtUtc</c>: it is source-sample/event activity time, not collection-loop liveness — quiet or
/// unchanged-value sources (MQTT/OPC UA) can be silent well past the window, source timestamps carry clock
/// skew, and a never-sampled connector has none. Deriving a stall needs an explicit expected-cadence fact
/// the protocol does not yet provide.
/// </para>
/// </summary>
public static class ConnectorCollectionHealthEvaluator
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>heartbeat stopped arriving (aged out) — the connector-host is no longer reporting: a real disconnect (断线).</summary>
    public const string StaleReasonOffline = "offline";

    /// <summary>still heartbeating but self-reported a terminal down state — an abnormal stop of unknown cause (异常停止), not a device disconnect.</summary>
    public const string StaleReasonFault = "fault";

    public static string DeriveStatus(
        InstanceHeartbeat? heartbeat, ConnectorCollectionHealthProjection? health, string reportedStatus, string healthStatus, DateTimeOffset now)
        => DeriveStatusAndReason(heartbeat, health, reportedStatus, healthStatus, now).Status;

    /// <summary>
    /// Derives status and, when stale, why. A genuine disconnect (heartbeat gone) takes precedence over a
    /// self-reported fault so the UI can label and time a real 断线 distinctly from an abnormal stop.
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
        if (heartbeatMissing)
        {
            return ("stale", StaleReasonOffline);
        }

        var reportedFault = string.Equals(reportedStatus, "stopped", StringComparison.OrdinalIgnoreCase)
            || string.Equals(healthStatus, "unhealthy", StringComparison.OrdinalIgnoreCase);
        if (reportedFault)
        {
            return ("stale", StaleReasonFault);
        }

        // Heartbeating and running, but never produced any collection fact (no received counter and no sample):
        // a connector with no configured mapping / nothing collected yet. Do not assert it is actively
        // collecting — report unknown (not-configured) rather than counting it as online.
        var noSamplingEvidence = health.ReceivedCount is null && health.LastSampleAtUtc is null;
        return noSamplingEvidence ? ("unknown", null) : ("current", null);
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
