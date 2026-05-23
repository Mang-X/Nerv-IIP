using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;

public partial record CountExecutionId : IGuidStronglyTypedId;

public enum CountExecutionStatus
{
    Open = 0,
    Completed = 1,
}

public sealed class CountExecution : Entity<CountExecutionId>, IAggregateRoot
{
    private CountExecution()
    {
    }

    private CountExecution(
        string organizationId,
        string environmentId,
        string countNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        decimal expectedQuantity)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        CountNo = WmsText.Required(countNo, nameof(countNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        LocationCode = WmsText.Required(locationCode, nameof(locationCode));
        ExpectedQuantity = expectedQuantity;
        Status = CountExecutionStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CountNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public decimal? VarianceQuantity { get; private set; }
    public CountExecutionStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static CountExecution Create(
        string organizationId,
        string environmentId,
        string countNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        decimal expectedQuantity)
    {
        return new CountExecution(organizationId, environmentId, countNo, skuCode, uomCode, siteCode, locationCode, expectedQuantity);
    }

    public void Complete(decimal countedQuantity)
    {
        if (Status == CountExecutionStatus.Completed)
        {
            throw new InvalidOperationException("Completed count executions are immutable.");
        }

        CountedQuantity = countedQuantity;
        VarianceQuantity = countedQuantity - ExpectedQuantity;
        Status = CountExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new CountExecutionCompletedDomainEvent(this));
    }
}
