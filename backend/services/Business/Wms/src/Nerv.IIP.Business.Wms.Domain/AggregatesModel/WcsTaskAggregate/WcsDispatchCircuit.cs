namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

public partial record WcsDispatchCircuitId : IGuidStronglyTypedId;

public sealed class WcsDispatchCircuit : Entity<WcsDispatchCircuitId>, IAggregateRoot
{
    private WcsDispatchCircuit()
    {
    }

    private WcsDispatchCircuit(string organizationId, string environmentId, string adapterType, string deviceId)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        AdapterType = WmsText.Required(adapterType, nameof(adapterType)).ToLowerInvariant();
        DeviceId = WmsText.Required(deviceId, nameof(deviceId));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string AdapterType { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public int ConsecutiveFailureCount { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? LastFailureAtUtc { get; private set; }
    public DateTime? ResetAtUtc { get; private set; }
    public bool IsOpen => OpenedAtUtc is not null;
    public string? RejectionReason => IsOpen
        ? $"WCS dispatch circuit is open for adapter '{AdapterType}' and device '{DeviceId}'."
        : null;

    public static WcsDispatchCircuit Create(string organizationId, string environmentId, string adapterType, string deviceId) =>
        new(organizationId, environmentId, adapterType, deviceId);

    public void RecordFailure(DateTime occurredAtUtc, int failureThreshold)
    {
        if (failureThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        }

        ConsecutiveFailureCount++;
        LastFailureAtUtc = EnsureUtc(occurredAtUtc);
        if (ConsecutiveFailureCount >= failureThreshold && OpenedAtUtc is null)
        {
            OpenedAtUtc = LastFailureAtUtc;
        }
    }

    public void RecordSuccess()
    {
        if (!IsOpen)
        {
            ConsecutiveFailureCount = 0;
        }
    }

    public void Reset(DateTime resetAtUtc)
    {
        ConsecutiveFailureCount = 0;
        OpenedAtUtc = null;
        ResetAtUtc = EnsureUtc(resetAtUtc);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : value.ToUniversalTime();
}
