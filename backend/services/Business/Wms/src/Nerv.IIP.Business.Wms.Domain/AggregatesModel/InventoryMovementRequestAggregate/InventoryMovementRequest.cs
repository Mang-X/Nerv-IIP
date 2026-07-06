using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;

public partial record InventoryMovementRequestId : IGuidStronglyTypedId;

public enum InventoryMovementRequestStatus
{
    Pending = 0,
    Posted = 1,
    Failed = 2,
}

public sealed class InventoryMovementRequest : Entity<InventoryMovementRequestId>, IAggregateRoot
{
    private InventoryMovementRequest()
    {
    }

    private InventoryMovementRequest(
        string organizationId,
        string environmentId,
        string movementType,
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
        string? inventoryReservationId,
        DateOnly? productionDate,
        DateOnly? expiryDate)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        MovementType = WmsText.Required(movementType, nameof(movementType)).ToLowerInvariant();
        SourceDocumentId = WmsText.Required(sourceDocumentId, nameof(sourceDocumentId));
        SourceDocumentLineId = WmsText.Optional(sourceDocumentLineId);
        IdempotencyKey = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        LocationCode = WmsText.Required(locationCode, nameof(locationCode));
        LotNo = WmsText.Optional(lotNo);
        SerialNo = WmsText.Optional(serialNo);
        QualityStatus = WmsText.Required(qualityStatus, nameof(qualityStatus)).ToLowerInvariant();
        OwnerType = WmsText.Required(ownerType, nameof(ownerType)).ToLowerInvariant();
        OwnerId = WmsText.Optional(ownerId);
        Quantity = WmsText.NonZero(quantity, nameof(quantity));
        InventoryReservationId = WmsText.Optional(inventoryReservationId);
        ProductionDate = productionDate;
        ExpiryDate = expiryDate;
        Status = InventoryMovementRequestStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new InventoryMovementRequestCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string MovementType { get; private set; } = string.Empty;
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
    public string? InventoryReservationId { get; private set; }
    public DateOnly? ProductionDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>
    /// Signed movement quantity sent to Inventory. Inbound increases stock, outbound decreases stock,
    /// and count-adjustment uses the counted-minus-expected variance.
    /// </summary>
    public decimal Quantity { get; private set; }
    public InventoryMovementRequestStatus Status { get; private set; }
    public string? InventoryMovementId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }

    public static InventoryMovementRequest Create(
        string organizationId,
        string environmentId,
        string movementType,
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
        string? inventoryReservationId = null,
        DateOnly? ProductionDate = null,
        DateOnly? ExpiryDate = null)
    {
        return new InventoryMovementRequest(
            organizationId,
            environmentId,
            movementType,
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
            inventoryReservationId,
            ProductionDate,
            ExpiryDate);
    }

    public void MarkPosted(string inventoryMovementId)
    {
        var requiredMovementId = WmsText.Required(inventoryMovementId, nameof(inventoryMovementId));
        if (Status == InventoryMovementRequestStatus.Posted)
        {
            if (InventoryMovementId == requiredMovementId)
            {
                return;
            }

            throw new InvalidOperationException("Inventory movement request was already posted with a different Inventory movement id.");
        }

        InventoryMovementId = requiredMovementId;
        Status = InventoryMovementRequestStatus.Posted;
        FailureCode = null;
        FailureMessage = null;
        PostedAtUtc = DateTime.UtcNow;
    }

    // Future posting-failed consumers should enter through this method; the current async posting flow
    // leaves requests Pending when Inventory handling fails and relies on CAP retry/DLQ/replay.
    public void MarkFailed(string failureCode, string failureMessage)
    {
        if (Status == InventoryMovementRequestStatus.Posted)
        {
            return;
        }

        var requiredFailureCode = WmsText.Required(failureCode, nameof(failureCode));
        var requiredFailureMessage = WmsText.Required(failureMessage, nameof(failureMessage));
        if (Status == InventoryMovementRequestStatus.Failed
            && FailureCode == requiredFailureCode
            && FailureMessage == requiredFailureMessage)
        {
            return;
        }

        FailureCode = requiredFailureCode;
        FailureMessage = requiredFailureMessage;
        Status = InventoryMovementRequestStatus.Failed;
    }
}
