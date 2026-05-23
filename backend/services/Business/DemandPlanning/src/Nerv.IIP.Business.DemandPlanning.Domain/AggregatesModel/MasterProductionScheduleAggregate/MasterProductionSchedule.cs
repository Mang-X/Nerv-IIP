namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;

public partial record MasterProductionScheduleId : IGuidStronglyTypedId;

public sealed class MasterProductionSchedule : Entity<MasterProductionScheduleId>, IAggregateRoot
{
    private MasterProductionSchedule()
    {
    }

    private MasterProductionSchedule(
        string organizationId,
        string environmentId,
        string skuCode,
        string uomCode,
        string siteCode,
        DateOnly bucketDate,
        decimal quantity)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        BucketDate = bucketDate;
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public DateOnly BucketDate { get; private set; }
    public decimal Quantity { get; private set; }

    public static MasterProductionSchedule Create(
        string organizationId,
        string environmentId,
        string skuCode,
        string uomCode,
        string siteCode,
        DateOnly bucketDate,
        decimal quantity)
    {
        return new MasterProductionSchedule(organizationId, environmentId, skuCode, uomCode, siteCode, bucketDate, quantity);
    }
}
