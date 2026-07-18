namespace Nerv.IIP.AppHub.Web.Application.Connectors;

public sealed class ConnectorCollectionHealthOptions
{
    public const string SectionName = "CollectionHealth";
    public static readonly TimeSpan MaximumBackendDeadline = TimeSpan.FromSeconds(8);

    public TimeSpan HostHeartbeatCadence { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan HostLivenessTimeout { get; set; } = TimeSpan.FromSeconds(6);
    public TimeSpan BackendDeadline { get; set; } = TimeSpan.FromSeconds(8);

    public bool HasValidHostLivenessWindow()
    {
        return HostHeartbeatCadence > TimeSpan.Zero
            && HostHeartbeatCadence.Ticks <= HostLivenessTimeout.Ticks / 3
            && HostLivenessTimeout <= BackendDeadline
            && BackendDeadline > TimeSpan.Zero
            && BackendDeadline <= MaximumBackendDeadline;
    }
}
