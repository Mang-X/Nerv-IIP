using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;

public partial record EngineeringItemId : IGuidStronglyTypedId;

public sealed class EngineeringItem : Entity<EngineeringItemId>, IAggregateRoot
{
    private EngineeringItem()
    {
    }

    private EngineeringItem(
        string organizationId,
        string environmentId,
        string itemCode,
        string revision,
        string name,
        EngineeringVersionStatus status)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        ItemCode = Required(itemCode);
        Revision = Required(revision);
        Name = Required(name);
        Status = status;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ItemCode { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public EngineeringVersionStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static EngineeringItem CreateRevision(
        string organizationId,
        string environmentId,
        string itemCode,
        string revision,
        string name,
        bool release)
    {
        return new EngineeringItem(
            organizationId,
            environmentId,
            itemCode,
            revision,
            name,
            release ? EngineeringVersionStatus.Published : EngineeringVersionStatus.Draft);
    }

    public void Rename(string name)
    {
        EnsureDraft();
        Name = Required(name);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != EngineeringVersionStatus.Draft)
        {
            throw new InvalidOperationException("Released or archived engineering item cannot be changed directly.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
