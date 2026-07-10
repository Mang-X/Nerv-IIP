using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;

public partial record StockReservationId : IGuidStronglyTypedId;

public sealed class StockReservation : Entity<StockReservationId>, IAggregateRoot
{
    private StockReservation()
    {
    }

    private StockReservation(
        StockLedger ledger,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        decimal quantity,
        DateTime? expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        OrganizationId = ledger.OrganizationId;
        EnvironmentId = ledger.EnvironmentId;
        SourceService = InventoryText.Required(sourceService);
        SourceDocumentId = InventoryText.Required(sourceDocumentId);
        SourceDocumentLineId = InventoryText.Optional(sourceDocumentLineId);
        IdempotencyKey = InventoryText.Required(idempotencyKey);
        SkuCode = ledger.SkuCode;
        UomCode = ledger.UomCode;
        SiteCode = ledger.SiteCode;
        LocationCode = ledger.LocationCode;
        LotNo = ledger.LotNo;
        SerialNo = ledger.SerialNo;
        QualityStatus = ledger.QualityStatus;
        OwnerType = ledger.OwnerType;
        OwnerId = ledger.OwnerId;
        ProductionDate = ledger.ProductionDate;
        ExpiryDate = ledger.ExpiryDate;
        ReservedQuantity = Positive(quantity, nameof(quantity));
        OpenQuantity = ReservedQuantity;
        Status = "open";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        ExpiresAtUtc = NormalizeFutureUtc(expiresAtUtc ?? CreatedAtUtc.AddHours(4), nameof(expiresAtUtc));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SourceService { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string? SourceDocumentLineId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public DateOnly? ProductionDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public decimal ReleasedQuantity { get; private set; }
    public decimal AllocatedQuantity { get; private set; }
    public decimal OpenQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public RowVersion RowVersion { get; private set; } = new(0);

    public static StockReservation Reserve(
        StockLedger ledger,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        decimal quantity,
        DateTime? expiresAtUtc = null)
    {
        return new StockReservation(ledger, sourceService, sourceDocumentId, sourceDocumentLineId, idempotencyKey, quantity, expiresAtUtc);
    }

    public void Release(decimal quantity)
    {
        var releaseQuantity = Positive(quantity, nameof(quantity));
        if (releaseQuantity > OpenQuantity)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.ReservationAllocationRejected,
                "Cannot release more than the open committed quantity.");
        }

        ReleasedQuantity += releaseQuantity;
        OpenQuantity -= releaseQuantity;
        Status = OpenQuantity == 0 ? "released" : "partially-released";
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Allocate(decimal quantity)
    {
        var allocateQuantity = Positive(quantity, nameof(quantity));
        if (allocateQuantity > OpenQuantity)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.ReservationAllocationRejected,
                "Cannot allocate more than the open committed quantity.");
        }

        AllocatedQuantity += allocateQuantity;
        OpenQuantity -= allocateQuantity;
        Status = OpenQuantity == 0 ? "allocated" : "partially-allocated";
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Renew(DateTime expiresAtUtc, DateTime renewedAtUtc)
    {
        if (OpenQuantity <= 0m)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.ReservationAllocationRejected,
                "Only an open reservation can be renewed.");
        }

        var normalizedRenewedAtUtc = NormalizeUtc(renewedAtUtc, nameof(renewedAtUtc));
        if (ExpiresAtUtc <= normalizedRenewedAtUtc)
        {
            throw new InventoryDomainException(
                InventoryDomainFailureReason.ReservationAllocationRejected,
                "An expired reservation cannot be renewed.");
        }

        var normalizedExpiryUtc = NormalizeFutureUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (normalizedExpiryUtc <= ExpiresAtUtc)
        {
            return;
        }

        ExpiresAtUtc = normalizedExpiryUtc;
        UpdatedAtUtc = normalizedRenewedAtUtc;
    }

    public decimal Expire(DateTime expiredAtUtc)
    {
        var normalizedExpiredAtUtc = NormalizeUtc(expiredAtUtc, nameof(expiredAtUtc));
        if (OpenQuantity <= 0m || ExpiresAtUtc > normalizedExpiredAtUtc)
        {
            return 0m;
        }

        var expiredQuantity = OpenQuantity;
        ReleasedQuantity += expiredQuantity;
        OpenQuantity = 0m;
        Status = "expired";
        UpdatedAtUtc = normalizedExpiredAtUtc;
        this.AddDomainEvent(new StockReservationExpiredDomainEvent(this, expiredQuantity, normalizedExpiredAtUtc));
        return expiredQuantity;
    }

    public bool HasSamePayload(StockReservation other)
    {
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && SourceService == other.SourceService
            && SourceDocumentId == other.SourceDocumentId
            && SourceDocumentLineId == other.SourceDocumentLineId
            && IdempotencyKey == other.IdempotencyKey
            && SkuCode == other.SkuCode
            && UomCode == other.UomCode
            && SiteCode == other.SiteCode
            && LocationCode == other.LocationCode
            && LotNo == other.LotNo
            && SerialNo == other.SerialNo
            && QualityStatus == other.QualityStatus
            && OwnerType == other.OwnerType
            && OwnerId == other.OwnerId
            && ProductionDate == other.ProductionDate
            && ExpiryDate == other.ExpiryDate
            && ReservedQuantity == other.ReservedQuantity;
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, "Reservation quantity must be positive.") : value;
    }

    private static DateTime NormalizeFutureUtc(DateTime value, string parameterName)
    {
        var normalized = NormalizeUtc(value, parameterName);
        if (normalized <= DateTime.UtcNow)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Reservation expiration must be in the future.");
        }

        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value, string parameterName)
    {
        _ = parameterName;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
