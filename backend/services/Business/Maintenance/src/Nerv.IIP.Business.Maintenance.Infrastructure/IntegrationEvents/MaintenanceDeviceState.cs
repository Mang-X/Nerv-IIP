namespace Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;

public sealed class MaintenanceDeviceState
{
    private MaintenanceDeviceState()
    {
    }

    private MaintenanceDeviceState(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        bool disabled,
        DateTimeOffset changedAtUtc,
        string sourceEventId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        DeviceAssetId = deviceAssetId;
        Disabled = disabled;
        ChangedAtUtc = changedAtUtc.ToUniversalTime();
        SourceEventId = sourceEventId;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;

    public static MaintenanceDeviceState Create(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        bool disabled,
        DateTimeOffset changedAtUtc,
        string sourceEventId)
    {
        return new MaintenanceDeviceState(organizationId, environmentId, deviceAssetId, disabled, changedAtUtc, sourceEventId);
    }

    public bool Apply(bool disabled, DateTimeOffset changedAtUtc, string sourceEventId)
    {
        changedAtUtc = changedAtUtc.ToUniversalTime();
        if (changedAtUtc < ChangedAtUtc)
        {
            return false;
        }

        Disabled = disabled;
        ChangedAtUtc = changedAtUtc;
        SourceEventId = sourceEventId;
        return true;
    }
}
