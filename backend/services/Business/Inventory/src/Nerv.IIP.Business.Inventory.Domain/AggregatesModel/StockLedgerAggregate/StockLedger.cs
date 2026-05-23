using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;

public partial record StockLedgerId : IGuidStronglyTypedId;

public sealed class StockLedger : Entity<StockLedgerId>, IAggregateRoot
{
    private readonly List<StockMovement> appliedMovements = [];

    private StockLedger()
    {
    }

    private StockLedger(
        string organizationId,
        string environmentId,
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
        OrganizationId = InventoryText.Required(organizationId);
        EnvironmentId = InventoryText.Required(environmentId);
        SkuCode = InventoryText.Required(skuCode);
        UomCode = InventoryText.Required(uomCode);
        SiteCode = InventoryText.Required(siteCode);
        LocationCode = InventoryText.Required(locationCode);
        LotNo = InventoryText.Optional(lotNo);
        SerialNo = InventoryText.Optional(serialNo);
        QualityStatus = InventoryText.Required(qualityStatus).ToLowerInvariant();
        OwnerType = InventoryText.Required(ownerType).ToLowerInvariant();
        OwnerId = InventoryText.Optional(ownerId);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public decimal OnHandQuantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public decimal AvailableQuantity => OnHandQuantity - ReservedQuantity;
    public long LedgerVersion { get; private set; }
    public RowVersion RowVersion { get; private set; } = new(0);
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<StockMovement> AppliedMovements => appliedMovements;

    public static StockLedger Create(
        string organizationId,
        string environmentId,
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
        return new StockLedger(
            organizationId,
            environmentId,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            qualityStatus,
            ownerType,
            ownerId);
    }

    public StockMovement ApplyMovement(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        EnsureSameDimension(movement);

        var duplicate = appliedMovements.SingleOrDefault(x =>
            x.OrganizationId == movement.OrganizationId
            && x.EnvironmentId == movement.EnvironmentId
            && x.SourceService == movement.SourceService
            && x.SourceDocumentId == movement.SourceDocumentId
            && x.IdempotencyKey == movement.IdempotencyKey);
        if (duplicate is not null)
        {
            return duplicate.HasSamePayload(movement)
                ? duplicate
                : throw new InvalidOperationException("Duplicate idempotency key conflicts with the existing stock movement payload.");
        }

        var nextOnHand = OnHandQuantity + movement.Quantity;
        if (nextOnHand < 0)
        {
            throw new InvalidOperationException("Stock movement would make on-hand quantity negative.");
        }

        OnHandQuantity = nextOnHand;
        LedgerVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
        appliedMovements.Add(movement);
        this.AddDomainEvent(new StockAvailabilityChangedDomainEvent(this));
        return movement;
    }

    private void EnsureSameDimension(StockMovement movement)
    {
        if (OrganizationId != movement.OrganizationId
            || EnvironmentId != movement.EnvironmentId
            || SkuCode != movement.SkuCode
            || UomCode != movement.UomCode
            || SiteCode != movement.SiteCode
            || LocationCode != movement.LocationCode
            || LotNo != movement.LotNo
            || SerialNo != movement.SerialNo
            || QualityStatus != movement.QualityStatus
            || OwnerType != movement.OwnerType
            || OwnerId != movement.OwnerId)
        {
            throw new InvalidOperationException("Stock movement dimensions do not match the ledger dimensions.");
        }
    }
}
