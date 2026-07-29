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
        decimal expectedQuantity,
        string? assignedOperatorUserId,
        string? assignedPoolCode)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        CountNo = WmsText.Required(countNo, nameof(countNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        LocationCode = WmsText.Required(locationCode, nameof(locationCode));
        AssignedOperatorUserId = WmsText.Optional(assignedOperatorUserId);
        AssignedPoolCode = WmsText.Optional(assignedPoolCode);
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
    public string? AssignedOperatorUserId { get; private set; }
    public string? AssignedPoolCode { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public decimal? VarianceQuantity { get; private set; }
    public string? InventoryCountTaskId { get; private set; }
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
        decimal expectedQuantity,
        string? assignedOperatorUserId = null,
        string? assignedPoolCode = null)
    {
        return new CountExecution(
            organizationId,
            environmentId,
            countNo,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            expectedQuantity,
            assignedOperatorUserId,
            assignedPoolCode);
    }

    public void AssignWorkPool(string assignedPoolCode, string? assignedOperatorUserId = null)
    {
        if (Status != CountExecutionStatus.Open)
        {
            throw new InvalidOperationException("Completed count executions cannot be reassigned.");
        }

        AssignedPoolCode = WmsText.Required(assignedPoolCode, nameof(assignedPoolCode));
        AssignedOperatorUserId = WmsText.Optional(assignedOperatorUserId);
    }

    public void MarkInventoryCountTaskCreated(string inventoryCountTaskId)
    {
        var normalizedCountTaskId = WmsText.Required(inventoryCountTaskId, nameof(inventoryCountTaskId));
        if (InventoryCountTaskId is not null && InventoryCountTaskId != normalizedCountTaskId)
        {
            throw new InvalidOperationException("Count execution already has a different Inventory count task id.");
        }

        InventoryCountTaskId = normalizedCountTaskId;
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
