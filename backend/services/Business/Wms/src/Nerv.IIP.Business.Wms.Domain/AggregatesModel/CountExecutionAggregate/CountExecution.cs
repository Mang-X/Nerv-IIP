using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;

public partial record CountExecutionId : IGuidStronglyTypedId;

public enum CountExecutionStatus
{
    Open = 0,
    Completed = 1,
    PendingApproval = 2,
    RecountRequired = 3,
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
        string? assignedPoolCode,
        string? lotNo)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        CountNo = WmsText.Required(countNo, nameof(countNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        LocationCode = WmsText.Required(locationCode, nameof(locationCode));
        LotNo = WmsText.Optional(lotNo);
        AssignedOperatorUserId = WmsText.Optional(assignedOperatorUserId);
        AssignedPoolCode = WmsText.Optional(assignedPoolCode);
        ExpectedQuantity = expectedQuantity;
        Status = CountExecutionStatus.Open;
        Version = 1;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CountNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? AssignedOperatorUserId { get; private set; }
    public string? AssignedPoolCode { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public decimal? VarianceQuantity { get; private set; }
    public string? InventoryCountTaskId { get; private set; }
    public CountExecutionStatus Status { get; private set; }
    public long Version { get; private set; }
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
        string? assignedPoolCode = null,
        string? lotNo = null)
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
            assignedPoolCode,
            lotNo);
    }

    public void AssignWorkPool(
        string assignedPoolCode,
        string? assignedOperatorUserId,
        long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        if (Status != CountExecutionStatus.Open)
        {
            throw new InvalidOperationException("Completed count executions cannot be reassigned.");
        }

        AssignedPoolCode = WmsText.Required(assignedPoolCode, nameof(assignedPoolCode));
        AssignedOperatorUserId = WmsText.Optional(assignedOperatorUserId);
        AdvanceVersion();
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

    public void SubmitForApproval(decimal countedQuantity, long expectedVersion, decimal inventoryVariance)
    {
        RecordCount(countedQuantity, expectedVersion, inventoryVariance);
        Status = CountExecutionStatus.PendingApproval;
        AdvanceVersion();
    }

    public void Complete(decimal countedQuantity, long expectedVersion, decimal? inventoryVariance = null)
    {
        RecordCount(countedQuantity, expectedVersion, inventoryVariance);
        MarkCompleted(DateTime.UtcNow);
    }

    public void ApplyPostedAdjustment(DateTime postedAtUtc, decimal inventoryVariance)
    {
        if (Status != CountExecutionStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only pending count executions can accept an approval posting.");
        }

        ExpectedQuantity = CountedQuantity!.Value - inventoryVariance;
        VarianceQuantity = inventoryVariance;
        MarkCompleted(postedAtUtc);
    }

    public void RequireRecount()
    {
        if (Status != CountExecutionStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only pending count executions can require recount after approval.");
        }

        Status = CountExecutionStatus.RecountRequired;
        AdvanceVersion();
    }

    private void MarkCompleted(DateTime postedAtUtc)
    {
        Status = CountExecutionStatus.Completed;
        CompletedAtUtc = postedAtUtc;
        AdvanceVersion();
        this.AddDomainEvent(new CountExecutionCompletedDomainEvent(this));
    }

    private void RecordCount(decimal countedQuantity, long expectedVersion, decimal? inventoryVariance)
    {
        EnsureExpectedVersion(expectedVersion);
        if (Status != CountExecutionStatus.Open)
        {
            throw new InvalidOperationException("Only open count executions may accept counted quantities.");
        }

        if (countedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(countedQuantity),
                countedQuantity,
                "Counted quantity cannot be negative.");
        }

        CountedQuantity = countedQuantity;
        if (inventoryVariance is { } variance)
        {
            ExpectedQuantity = countedQuantity - variance;
        }

        VarianceQuantity = inventoryVariance ?? countedQuantity - ExpectedQuantity;
    }

    private void EnsureExpectedVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Count execution version conflict: expected {expectedVersion}, actual {Version}.");
        }
    }

    private void AdvanceVersion()
    {
        Version = checked(Version + 1);
    }
}
