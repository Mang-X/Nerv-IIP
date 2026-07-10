using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

public partial record StockCountTaskId : IGuidStronglyTypedId;

public static class StockCountTaskStatuses
{
    public const string Open = "open";
    public const string PendingApproval = "pending-approval";
    public const string Confirmed = "confirmed";
    public const string RecountRequired = "recount-required";
    public const string Cancelled = "cancelled";
}

public sealed class StockCountTask : Entity<StockCountTaskId>, IAggregateRoot
{
    private StockCountTask()
    {
    }

    private StockCountTask(
        string organizationId,
        string environmentId,
        string countTaskCode,
        string idempotencyKey,
        string ledgerOrganizationId,
        string ledgerEnvironmentId,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId,
        long expectedLedgerVersion)
    {
        OrganizationId = InventoryText.Required(organizationId);
        EnvironmentId = InventoryText.Required(environmentId);
        CountTaskCode = InventoryText.Required(countTaskCode);
        IdempotencyKey = InventoryText.Required(idempotencyKey);
        LedgerOrganizationId = InventoryText.Required(ledgerOrganizationId);
        LedgerEnvironmentId = InventoryText.Required(ledgerEnvironmentId);
        SkuCode = InventoryText.Required(skuCode);
        UomCode = InventoryText.Required(uomCode);
        SiteCode = InventoryText.Required(siteCode);
        LocationCode = InventoryText.Required(locationCode);
        LotNo = InventoryText.Optional(lotNo);
        SerialNo = InventoryText.Optional(serialNo);
        QualityStatus = StockQualityStatus.Normalize(qualityStatus);
        OwnerType = StockOwnerType.Normalize(ownerType);
        OwnerId = InventoryText.Optional(ownerId);
        ExpectedLedgerVersion = expectedLedgerVersion;
        Status = StockCountTaskStatuses.Open;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CountTaskCode { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string LedgerOrganizationId { get; private set; } = string.Empty;
    public string LedgerEnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public long ExpectedLedgerVersion { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public decimal? VarianceQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static StockCountTask Create(
        string organizationId,
        string environmentId,
        string countTaskCode,
        string idempotencyKey,
        string ledgerOrganizationId,
        string ledgerEnvironmentId,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId,
        long expectedLedgerVersion)
    {
        return new StockCountTask(
            organizationId,
            environmentId,
            countTaskCode,
            idempotencyKey,
            ledgerOrganizationId,
            ledgerEnvironmentId,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            qualityStatus,
            ownerType,
            ownerId,
            expectedLedgerVersion);
    }

    public bool HasSameCreationScope(
        string countTaskCode,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId)
    {
        return CountTaskCode == InventoryText.Required(countTaskCode)
            && SkuCode == InventoryText.Required(skuCode)
            && UomCode == InventoryText.Required(uomCode)
            && SiteCode == InventoryText.Required(siteCode)
            && LocationCode == InventoryText.Required(locationCode)
            && LotNo == InventoryText.Optional(lotNo)
            && SerialNo == InventoryText.Optional(serialNo)
            && QualityStatus == StockQualityStatus.Normalize(qualityStatus)
            && OwnerType == StockOwnerType.Normalize(ownerType)
            && OwnerId == InventoryText.Optional(ownerId);
    }

    public StockMovement ConfirmAdjustment(StockLedger ledger, decimal countedQuantity, string idempotencyKey)
    {
        EnsureStatus(StockCountTaskStatuses.Open, "Only open stock count tasks can be confirmed.");
        return ApplyConfirmedAdjustment(ledger, countedQuantity, idempotencyKey);
    }

    public decimal SubmitForApproval(StockLedger ledger, decimal countedQuantity)
    {
        EnsureStatus(StockCountTaskStatuses.Open, "Only open stock count tasks can be submitted for approval.");
        EnsureReadyForAdjustment(ledger, countedQuantity);
        var variance = countedQuantity - ledger.OnHandQuantity;
        CountedQuantity = countedQuantity;
        VarianceQuantity = variance;
        Status = StockCountTaskStatuses.PendingApproval;
        UpdatedAtUtc = DateTime.UtcNow;
        return variance;
    }

    public StockMovement ConfirmApprovedAdjustment(StockLedger ledger, string idempotencyKey)
    {
        EnsureStatus(StockCountTaskStatuses.PendingApproval, "Only a pending stock count adjustment can be posted after approval.");
        return ApplyConfirmedAdjustment(
            ledger,
            CountedQuantity ?? throw new InvalidOperationException("Pending stock count approval has no counted quantity."),
            idempotencyKey);
    }

    public void RequireRecountAfterApprovalRejection(StockLedger ledger)
    {
        EnsureStatus(StockCountTaskStatuses.PendingApproval, "Only a pending stock count adjustment can require recount after approval rejection.");
        EnsureSameDimension(ledger);
        ledger.ReleaseCountFreeze();
        Status = StockCountTaskStatuses.RecountRequired;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(StockLedger ledger, string reason)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (Status == StockCountTaskStatuses.Confirmed)
        {
            throw new InvalidOperationException("Confirmed stock count task cannot be cancelled.");
        }

        if (Status == StockCountTaskStatuses.Cancelled)
        {
            return;
        }

        _ = InventoryText.Required(reason);
        EnsureSameDimension(ledger);
        ledger.ReleaseCountFreeze();
        Status = StockCountTaskStatuses.Cancelled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void EnsureSameDimension(StockLedger ledger)
    {
        if (LedgerOrganizationId != ledger.OrganizationId
            || LedgerEnvironmentId != ledger.EnvironmentId
            || SkuCode != ledger.SkuCode
            || UomCode != ledger.UomCode
            || SiteCode != ledger.SiteCode
            || LocationCode != ledger.LocationCode
            || LotNo != ledger.LotNo
            || SerialNo != ledger.SerialNo
            || QualityStatus != ledger.QualityStatus
            || OwnerType != ledger.OwnerType
            || OwnerId != ledger.OwnerId)
        {
            throw new InvalidOperationException("Stock count task dimensions do not match the ledger dimensions.");
        }
    }

    private StockMovement ApplyConfirmedAdjustment(StockLedger ledger, decimal countedQuantity, string idempotencyKey)
    {
        EnsureReadyForAdjustment(ledger, countedQuantity);
        var variance = countedQuantity - ledger.OnHandQuantity;
        var adjustment = StockMovement.Post(
            ledger.OrganizationId,
            ledger.EnvironmentId,
            "count-adjustment",
            "inventory",
            CountTaskCode,
            null,
            idempotencyKey,
            ledger.SkuCode,
            ledger.UomCode,
            ledger.SiteCode,
            ledger.LocationCode,
            ledger.LotNo,
            ledger.SerialNo,
            ledger.QualityStatus,
            ledger.OwnerType,
            ledger.OwnerId,
            variance);

        ledger.ApplyMovement(adjustment);
        CountedQuantity = countedQuantity;
        VarianceQuantity = variance;
        Status = StockCountTaskStatuses.Confirmed;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockCountVarianceConfirmedDomainEvent(this, adjustment));
        return adjustment;
    }

    private void EnsureReadyForAdjustment(StockLedger ledger, decimal countedQuantity)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (countedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countedQuantity), "Counted quantity cannot be negative.");
        }

        EnsureSameDimension(ledger);
        if (ledger.LedgerVersion != ExpectedLedgerVersion)
        {
            Status = StockCountTaskStatuses.RecountRequired;
            UpdatedAtUtc = DateTime.UtcNow;
            throw new StockCountRecountRequiredException("Stock count task requires recount because ledger version changed after the count snapshot.");
        }
    }

    private void EnsureStatus(string expectedStatus, string message)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException(message);
        }
    }
}
