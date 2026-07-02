namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;

public partial record DowntimeReasonId : IGuidStronglyTypedId;

public sealed class DowntimeReason : Entity<DowntimeReasonId>, IAggregateRoot
{
    private DowntimeReason()
    {
    }

    private DowntimeReason(string organizationId, string environmentId, string reasonCode, string description, string reasonCategory, string lossCategory)
    {
        Id = new DowntimeReasonId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        ReasonCode = MaintenanceText.Required(reasonCode, nameof(reasonCode));
        Description = MaintenanceText.Required(description, nameof(description));
        ReasonCategory = MaintenanceText.Required(reasonCategory, nameof(reasonCategory)).ToLowerInvariant();
        LossCategory = MaintenanceText.Required(lossCategory, nameof(lossCategory)).ToLowerInvariant();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string ReasonCategory { get; private set; } = string.Empty;
    public string LossCategory { get; private set; } = string.Empty;

    public static DowntimeReason Create(string organizationId, string environmentId, string reasonCode, string description, string reasonCategory = "unclassified", string lossCategory = "unclassified")
    {
        return new DowntimeReason(organizationId, environmentId, reasonCode, description, reasonCategory, lossCategory);
    }

    public void Update(string description, string reasonCategory, string lossCategory)
    {
        Description = MaintenanceText.Required(description, nameof(description));
        ReasonCategory = MaintenanceText.Required(reasonCategory, nameof(reasonCategory)).ToLowerInvariant();
        LossCategory = MaintenanceText.Required(lossCategory, nameof(lossCategory)).ToLowerInvariant();
    }
}
