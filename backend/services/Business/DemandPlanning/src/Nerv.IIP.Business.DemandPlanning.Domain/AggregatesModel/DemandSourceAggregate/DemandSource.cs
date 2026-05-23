using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;

public partial record DemandSourceId : IGuidStronglyTypedId;

public sealed class DemandSource : Entity<DemandSourceId>, IAggregateRoot
{
    private DemandSource()
    {
    }

    private DemandSource(
        string organizationId,
        string environmentId,
        string demandType,
        string sourceReference,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly dueDate)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        DemandType = DemandPlanningText.Required(demandType, nameof(demandType)).ToLowerInvariant();
        SourceReference = DemandPlanningText.Required(sourceReference, nameof(sourceReference));
        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        DueDate = dueDate;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new DemandSourceCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DemandType { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public DateOnly DueDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static DemandSource Create(
        string organizationId,
        string environmentId,
        string demandType,
        string sourceReference,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly dueDate)
    {
        return new DemandSource(organizationId, environmentId, demandType, sourceReference, skuCode, uomCode, siteCode, quantity, dueDate);
    }

    public void Update(decimal quantity, DateOnly dueDate)
    {
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        DueDate = dueDate;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
