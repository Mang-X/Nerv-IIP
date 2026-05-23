namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;

public partial record DowntimeReasonId : IGuidStronglyTypedId;

public sealed class DowntimeReason : Entity<DowntimeReasonId>, IAggregateRoot
{
    private DowntimeReason()
    {
    }

    private DowntimeReason(string organizationId, string environmentId, string reasonCode, string description)
    {
        Id = new DowntimeReasonId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        ReasonCode = MaintenanceText.Required(reasonCode, nameof(reasonCode));
        Description = MaintenanceText.Required(description, nameof(description));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public static DowntimeReason Create(string organizationId, string environmentId, string reasonCode, string description)
    {
        return new DowntimeReason(organizationId, environmentId, reasonCode, description);
    }
}
