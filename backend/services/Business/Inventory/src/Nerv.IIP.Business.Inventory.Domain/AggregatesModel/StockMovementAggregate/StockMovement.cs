using Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

public partial record StockMovementId : IGuidStronglyTypedId;

public sealed class StockMovement : Entity<StockMovementId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedMovementTypes =
    [
        "inbound",
        "outbound",
        "transfer",
        "adjustment",
        "count-adjustment",
        "status-transfer-out",
        "status-transfer-in",
    ];

    private StockMovement()
    {
    }

    private StockMovement(
        string organizationId,
        string environmentId,
        string movementType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId,
        decimal quantity,
        decimal? unitCost)
    {
        OrganizationId = InventoryText.Required(organizationId);
        EnvironmentId = InventoryText.Required(environmentId);
        MovementType = InventoryText.Supported(movementType, SupportedMovementTypes, nameof(movementType));
        SourceService = InventoryText.Required(sourceService);
        SourceDocumentId = InventoryText.Required(sourceDocumentId);
        SourceDocumentLineId = InventoryText.Optional(sourceDocumentLineId);
        IdempotencyKey = InventoryText.Required(idempotencyKey);
        SkuCode = InventoryText.Required(skuCode);
        UomCode = InventoryText.Required(uomCode);
        SiteCode = InventoryText.Required(siteCode);
        LocationCode = InventoryText.Required(locationCode);
        LotNo = InventoryText.Optional(lotNo);
        SerialNo = InventoryText.Optional(serialNo);
        QualityStatus = StockQualityStatus.Normalize(qualityStatus);
        OwnerType = InventoryText.Required(ownerType).ToLowerInvariant();
        OwnerId = InventoryText.Optional(ownerId);
        Quantity = NonZero(quantity, nameof(quantity));
        UnitCost = unitCost is null ? null : NonNegative(unitCost.Value, nameof(unitCost));
        MovementAmount = UnitCost * Quantity;
        PostedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new StockMovementPostedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string MovementType { get; private set; } = string.Empty;
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
    public decimal Quantity { get; private set; }
    public decimal? UnitCost { get; private set; }
    public decimal? MovementAmount { get; private set; }
    public DateTime PostedAtUtc { get; private set; }

    public static StockMovement Post(
        string organizationId,
        string environmentId,
        string movementType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string idempotencyKey,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string qualityStatus,
        string ownerType,
        string? ownerId,
        decimal quantity,
        decimal? unitCost = null)
    {
        return new StockMovement(
            organizationId,
            environmentId,
            movementType,
            sourceService,
            sourceDocumentId,
            sourceDocumentLineId,
            idempotencyKey,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            qualityStatus,
            ownerType,
            ownerId,
            quantity,
            unitCost);
    }

    public void ApplyValuation(decimal unitCost)
    {
        var valuationUnitCost = NonNegative(unitCost, nameof(unitCost));
        if (UnitCost is not null || Quantity < 0)
        {
            UnitCost = valuationUnitCost;
        }

        MovementAmount = valuationUnitCost * Quantity;
    }

    public bool HasSamePayload(StockMovement other)
    {
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && MovementType == other.MovementType
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
            && Quantity == other.Quantity
            && UnitCost == other.UnitCost;
    }

    private static decimal NonZero(decimal value, string parameterName)
    {
        return value == 0 ? throw new ArgumentOutOfRangeException(parameterName, "Quantity cannot be zero.") : value;
    }

    private static decimal NonNegative(decimal value, string parameterName)
    {
        return value < 0 ? throw new ArgumentOutOfRangeException(parameterName, "Unit cost cannot be negative.") : value;
    }
}
