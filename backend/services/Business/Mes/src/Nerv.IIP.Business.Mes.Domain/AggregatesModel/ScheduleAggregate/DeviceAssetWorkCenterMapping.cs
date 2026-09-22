namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;

public partial record DeviceAssetWorkCenterMappingId : IGuidStronglyTypedId;

public sealed class DeviceAssetWorkCenterMapping : Entity<DeviceAssetWorkCenterMappingId>, IAggregateRoot
{
    private DeviceAssetWorkCenterMapping()
    {
    }

    private DeviceAssetWorkCenterMapping(string? organizationId, string? environmentId, string deviceAssetId, string workCenterId)
    {
        OrganizationId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();
        EnvironmentId = string.IsNullOrWhiteSpace(environmentId) ? null : environmentId.Trim();
        DeviceAssetId = DomainGuard.Required(deviceAssetId, nameof(deviceAssetId));
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
    }

    public string? OrganizationId { get; private set; }
    public string? EnvironmentId { get; private set; }
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;

    public static DeviceAssetWorkCenterMapping Create(string deviceAssetId, string workCenterId)
    {
        return new DeviceAssetWorkCenterMapping(null, null, deviceAssetId, workCenterId);
    }

    public static DeviceAssetWorkCenterMapping Create(string organizationId, string environmentId, string deviceAssetId, string workCenterId)
    {
        return new DeviceAssetWorkCenterMapping(organizationId, environmentId, deviceAssetId, workCenterId);
    }

    public void Remap(string workCenterId)
    {
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
    }
}
