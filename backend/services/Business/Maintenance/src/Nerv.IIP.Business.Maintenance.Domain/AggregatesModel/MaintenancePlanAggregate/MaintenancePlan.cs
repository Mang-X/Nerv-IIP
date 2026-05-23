using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;

public partial record MaintenancePlanId : IGuidStronglyTypedId;

public sealed class MaintenancePlan : Entity<MaintenancePlanId>, IAggregateRoot
{
    private MaintenancePlan()
    {
    }

    private MaintenancePlan(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string planCode,
        string interval,
        DateOnly startsOn,
        string owner)
    {
        Id = new MaintenancePlanId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = MaintenanceText.Required(deviceAssetId, nameof(deviceAssetId));
        PlanCode = MaintenanceText.Required(planCode, nameof(planCode));
        Interval = MaintenanceText.Required(interval, nameof(interval));
        StartsOn = startsOn;
        Owner = MaintenanceText.Required(owner, nameof(owner));
        CreatedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new MaintenancePlanCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string PlanCode { get; private set; } = string.Empty;
    public string Interval { get; private set; } = string.Empty;
    public DateOnly StartsOn { get; private set; }
    public string Owner { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static MaintenancePlan Create(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string planCode,
        string interval,
        DateOnly startsOn,
        string owner)
    {
        return new MaintenancePlan(organizationId, environmentId, deviceAssetId, planCode, interval, startsOn, owner);
    }
}
