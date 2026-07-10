using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;

public partial record DeviceControlChannelBindingId : IGuidStronglyTypedId;

// Explicit routing binding that resolves which connector host + connector instance owns a device's
// control channel. Device control commands look this up by (org, env, deviceAssetId) so operators never
// supply infrastructure identifiers, and dispatch is blocked when no active binding exists.
public sealed class DeviceControlChannelBinding : Entity<DeviceControlChannelBindingId>, IAggregateRoot
{
    private DeviceControlChannelBinding()
    {
    }

    private DeviceControlChannelBinding(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string connectorHostId,
        string instanceKey)
    {
        Id = new DeviceControlChannelBindingId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        UpdateDefinition(connectorHostId, instanceKey);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new DeviceControlChannelBindingConfiguredDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string ConnectorHostId { get; private set; } = string.Empty;
    public string InstanceKey { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public string? DisabledReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static DeviceControlChannelBinding Configure(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string connectorHostId,
        string instanceKey)
    {
        return new DeviceControlChannelBinding(organizationId, environmentId, deviceAssetId, connectorHostId, instanceKey);
    }

    // Upsert of the routing definition. Re-activates the binding and clears any prior disable reason so a
    // maintainer re-submitting an edited binding brings the channel back into service.
    public void UpdateDefinition(string connectorHostId, string instanceKey)
    {
        ConnectorHostId = IndustrialTelemetryText.Required(connectorHostId, nameof(connectorHostId));
        InstanceKey = IndustrialTelemetryText.Required(instanceKey, nameof(instanceKey));
        IsActive = true;
        DisabledReason = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Disable(string? reason)
    {
        IsActive = false;
        DisabledReason = IndustrialTelemetryText.Optional(reason);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
