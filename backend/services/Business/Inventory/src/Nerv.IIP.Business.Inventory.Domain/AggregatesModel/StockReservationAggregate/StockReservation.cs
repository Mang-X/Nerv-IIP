using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;

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
        decimal quantity)
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
        ReservedQuantity = Positive(quantity, nameof(quantity));
        OpenQuantity = ReservedQuantity;
        Status = "open";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
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
    public decimal ReservedQuantity { get; private set; }
    public decimal ReleasedQuantity { get; private set; }
    public decimal AllocatedQuantity { get; private set; }
    public decimal OpenQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static StockReservation Reserve(
        StockLedger ledger,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        decimal quantity)
    {
        return new StockReservation(ledger, sourceService, sourceDocumentId, sourceDocumentLineId, idempotencyKey, quantity);
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
            && ReservedQuantity == other.ReservedQuantity;
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, "Reservation quantity must be positive.") : value;
    }
}
