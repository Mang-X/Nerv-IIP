namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;

public partial record MasterProductionScheduleId : IGuidStronglyTypedId;

public enum MasterProductionScheduleStatus
{
    Draft = 0,
    Reviewed = 1,
    Released = 2,
}

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
        Status = MasterProductionScheduleStatus.Draft;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public DateOnly BucketDate { get; private set; }
    public decimal Quantity { get; private set; }
    public MasterProductionScheduleStatus Status { get; private set; } = MasterProductionScheduleStatus.Draft;
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReleasedBy { get; private set; }
    public DateTimeOffset? ReleasedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

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

    public void Update(
        string skuCode,
        string uomCode,
        string siteCode,
        DateOnly bucketDate,
        decimal quantity)
    {
        if (Status == MasterProductionScheduleStatus.Released)
        {
            throw new InvalidOperationException("Released MPS buckets cannot be updated.");
        }

        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        BucketDate = bucketDate;
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        if (Status == MasterProductionScheduleStatus.Reviewed)
        {
            Status = MasterProductionScheduleStatus.Draft;
            ReviewedBy = null;
            ReviewedAtUtc = null;
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkReviewed(string reviewedBy)
    {
        if (Status == MasterProductionScheduleStatus.Released)
        {
            throw new InvalidOperationException("Released MPS buckets cannot be reviewed again.");
        }

        ReviewedBy = DemandPlanningText.Required(reviewedBy, nameof(reviewedBy));
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = MasterProductionScheduleStatus.Reviewed;
        UpdatedAtUtc = ReviewedAtUtc.Value;
    }

    public void Release(string releasedBy)
    {
        if (Status == MasterProductionScheduleStatus.Released)
        {
            return;
        }

        if (Status != MasterProductionScheduleStatus.Reviewed)
        {
            throw new InvalidOperationException("Only reviewed MPS buckets can be released.");
        }

        ReleasedBy = DemandPlanningText.Required(releasedBy, nameof(releasedBy));
        ReleasedAtUtc = DateTimeOffset.UtcNow;
        Status = MasterProductionScheduleStatus.Released;
        UpdatedAtUtc = ReleasedAtUtc.Value;
    }
}
