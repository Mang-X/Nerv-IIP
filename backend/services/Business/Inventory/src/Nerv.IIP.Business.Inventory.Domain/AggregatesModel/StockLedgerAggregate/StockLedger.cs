using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
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
        QualityStatus = StockQualityStatus.Normalize(qualityStatus);
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
    public decimal MovingAverageUnitCost { get; private set; }
    public decimal InventoryValue { get; private set; }
    public bool IsFrozenForCount { get; private set; }
    public string? FrozenCountTaskCode { get; private set; }
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
                : throw new InventoryDomainException(
                    InventoryDomainFailureReason.IdempotencyConflict,
                    "Duplicate idempotency key conflicts with the existing stock movement payload.");
        }

        var nextOnHand = OnHandQuantity + movement.Quantity;
        if (nextOnHand < 0)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.NegativeOnHand,
                "Stock movement would make on-hand quantity negative.");
        }

        if (movement.Quantity < 0 && nextOnHand < ReservedQuantity)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.ReservedStockProtection,
                "Stock movement would breach committed stock protection.");
        }

        if (IsFrozenForCount && movement.MovementType != "count-adjustment")
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.LedgerFrozen,
                $"Stock ledger is frozen for count task '{FrozenCountTaskCode}'.");
        }

        ApplyValuation(movement, nextOnHand);
        OnHandQuantity = nextOnHand;
        if (movement.MovementType == "count-adjustment")
        {
            ReleaseCountFreeze();
        }

        LedgerVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
        appliedMovements.Add(movement);
        this.AddDomainEvent(new StockAvailabilityChangedDomainEvent(this));
        return movement;
    }

    public void Reserve(StockReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsureSameDimension(reservation);
        if (IsFrozenForCount)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.LedgerFrozen,
                $"Stock ledger is frozen for count task '{FrozenCountTaskCode}'.");
        }

        if (reservation.ReservedQuantity > AvailableQuantity)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.ReservationAllocationRejected,
                "Reservation quantity exceeds available stock.");
        }

        ReservedQuantity += reservation.ReservedQuantity;
        LedgerVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockAvailabilityChangedDomainEvent(this));
    }

    public void ReleaseReservation(StockReservation reservation, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsureSameDimension(reservation);
        reservation.Release(quantity);
        ReservedQuantity -= quantity;
        if (ReservedQuantity < 0)
        {
            throw new InvalidOperationException("Reserved quantity cannot be negative.");
        }

        LedgerVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockAvailabilityChangedDomainEvent(this));
    }

    public void AllocateReservation(StockReservation reservation, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsureSameDimension(reservation);
        reservation.Allocate(quantity);
        ReservedQuantity -= quantity;
        if (ReservedQuantity < 0)
        {
            throw new InvalidOperationException("Reserved quantity cannot be negative.");
        }

        LedgerVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockAvailabilityChangedDomainEvent(this));
    }

    public void FreezeForCount(string countTaskCode)
    {
        if (IsFrozenForCount && FrozenCountTaskCode != countTaskCode)
        {
            throw new InvalidOperationException($"Stock ledger is already frozen for count task '{FrozenCountTaskCode}'.");
        }

        IsFrozenForCount = true;
        FrozenCountTaskCode = InventoryText.Required(countTaskCode);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ReleaseCountFreeze()
    {
        IsFrozenForCount = false;
        FrozenCountTaskCode = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void ApplyValuation(StockMovement movement, decimal nextOnHand)
    {
        if (movement.Quantity > 0)
        {
            var inboundUnitCost = movement.UnitCost ?? MovingAverageUnitCost;
            movement.ApplyValuation(inboundUnitCost);
            InventoryValue = RoundValuation(InventoryValue + (movement.MovementAmount ?? 0m));
            MovingAverageUnitCost = nextOnHand == 0 ? 0m : RoundValuation(InventoryValue / nextOnHand);
            return;
        }

        movement.ApplyValuation(MovingAverageUnitCost);
        InventoryValue = RoundValuation(InventoryValue + (movement.MovementAmount ?? 0m));
        if (nextOnHand == 0)
        {
            MovingAverageUnitCost = 0m;
            InventoryValue = 0m;
        }
    }

    private static decimal RoundValuation(decimal value) =>
        Math.Round(value, 6, MidpointRounding.ToEven);

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
            throw new InventoryDomainException(
                InventoryDomainFailureReason.DimensionMismatch,
                "Stock movement dimensions do not match the ledger dimensions.");
        }
    }

    private void EnsureSameDimension(StockReservation reservation)
    {
        if (OrganizationId != reservation.OrganizationId
            || EnvironmentId != reservation.EnvironmentId
            || SkuCode != reservation.SkuCode
            || UomCode != reservation.UomCode
            || SiteCode != reservation.SiteCode
            || LocationCode != reservation.LocationCode
            || LotNo != reservation.LotNo
            || SerialNo != reservation.SerialNo
            || QualityStatus != reservation.QualityStatus
            || OwnerType != reservation.OwnerType
            || OwnerId != reservation.OwnerId)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.DimensionMismatch,
                "Stock reservation dimensions do not match the ledger dimensions.");
        }
    }
}
