using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

public partial record StockCountTaskId : IGuidStronglyTypedId;

public sealed class StockCountTask : Entity<StockCountTaskId>, IAggregateRoot
{
    private StockCountTask()
    {
    }

    private StockCountTask(
        string organizationId,
        string environmentId,
        string countTaskCode,
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
        LedgerOrganizationId = InventoryText.Required(ledgerOrganizationId);
        LedgerEnvironmentId = InventoryText.Required(ledgerEnvironmentId);
        SkuCode = InventoryText.Required(skuCode);
        UomCode = InventoryText.Required(uomCode);
        SiteCode = InventoryText.Required(siteCode);
        LocationCode = InventoryText.Required(locationCode);
        LotNo = InventoryText.Optional(lotNo);
        SerialNo = InventoryText.Optional(serialNo);
        QualityStatus = StockQualityStatus.Normalize(qualityStatus);
        OwnerType = InventoryText.Required(ownerType).ToLowerInvariant();
        OwnerId = InventoryText.Optional(ownerId);
        ExpectedLedgerVersion = expectedLedgerVersion;
        Status = "open";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CountTaskCode { get; private set; } = string.Empty;
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

    public StockMovement ConfirmAdjustment(StockLedger ledger, decimal countedQuantity, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (Status == "confirmed")
        {
            throw new InvalidOperationException("Stock count task has already been confirmed.");
        }

        if (countedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countedQuantity), "Counted quantity cannot be negative.");
        }

        EnsureSameDimension(ledger);
        if (ledger.LedgerVersion != ExpectedLedgerVersion)
        {
            Status = "recount-required";
            UpdatedAtUtc = DateTime.UtcNow;
            throw new StockCountRecountRequiredException("Stock count task requires recount because ledger version changed after the count snapshot.");
        }

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
        Status = "confirmed";
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockCountVarianceConfirmedDomainEvent(this, adjustment));
        return adjustment;
    }

    public void Cancel(StockLedger ledger, string reason)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (Status == "confirmed")
        {
            throw new InvalidOperationException("Confirmed stock count task cannot be cancelled.");
        }

        if (Status == "cancelled")
        {
            return;
        }

        _ = InventoryText.Required(reason);
        EnsureSameDimension(ledger);
        ledger.ReleaseCountFreeze();
        Status = "cancelled";
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
}
