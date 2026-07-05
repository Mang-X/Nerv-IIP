using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;

public partial record DeviceStateSnapshotId : IGuidStronglyTypedId;

public sealed class DeviceStateSnapshot : Entity<DeviceStateSnapshotId>, IAggregateRoot
{
    private DeviceStateSnapshot()
    {
    }

    private DeviceStateSnapshot(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string state,
        DateTimeOffset occurredAtUtc,
        string sourceSequence,
        string? sourceSystem,
        string? sourceConnector,
        bool raiseChangedEvent)
    {
        Id = new DeviceStateSnapshotId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        State = IndustrialTelemetryText.RequiredLower(state, nameof(state));
        OccurredAtUtc = occurredAtUtc;
        OccurredAtUnixTimeMilliseconds = occurredAtUtc.ToUnixTimeMilliseconds();
        SourceSequence = IndustrialTelemetryText.Required(sourceSequence, nameof(sourceSequence));
        SourceSystem = IndustrialTelemetryText.Optional(sourceSystem);
        SourceConnector = IndustrialTelemetryText.Optional(sourceConnector);
        RecordedAtUtc = DateTimeOffset.UtcNow;
        RecordedAtUnixTimeMilliseconds = RecordedAtUtc.ToUnixTimeMilliseconds();
        if (raiseChangedEvent)
        {
            RaiseStateChangedEvent();
        }
    }

    public void RaiseStateChangedEvent()
    {
        this.AddDomainEvent(new DeviceStateChangedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public long OccurredAtUnixTimeMilliseconds { get; private set; }
    public string SourceSequence { get; private set; } = string.Empty;
    public string? SourceSystem { get; private set; }
    public string? SourceConnector { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public long RecordedAtUnixTimeMilliseconds { get; private set; }

    public static DeviceStateSnapshot Record(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string state,
        DateTimeOffset occurredAtUtc,
        string sourceSequence,
        string? sourceSystem = null,
        string? sourceConnector = null,
        bool raiseChangedEvent = true)
    {
        return new DeviceStateSnapshot(
            organizationId,
            environmentId,
            deviceAssetId,
            state,
            occurredAtUtc,
            sourceSequence,
            sourceSystem,
            sourceConnector,
            raiseChangedEvent);
    }

    public bool IsSameSourceSequence(DeviceStateSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && DeviceAssetId == other.DeviceAssetId
            && SourceSystem == other.SourceSystem
            && SourceConnector == other.SourceConnector
            && SourceSequence == other.SourceSequence;
    }

    public bool HasSamePayload(DeviceStateSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return IsSameSourceSequence(other)
            && State == other.State
            && OccurredAtUtc == other.OccurredAtUtc;
    }
}
