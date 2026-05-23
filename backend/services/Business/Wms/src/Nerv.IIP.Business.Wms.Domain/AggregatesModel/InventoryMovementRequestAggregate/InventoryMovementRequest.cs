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
        decimal quantity)
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
        Quantity = WmsText.Positive(quantity, nameof(quantity));
        Status = InventoryMovementRequestStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
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
        decimal quantity)
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
            quantity);
    }

    public void MarkPosted(string inventoryMovementId)
    {
        InventoryMovementId = WmsText.Required(inventoryMovementId, nameof(inventoryMovementId));
        Status = InventoryMovementRequestStatus.Posted;
        FailureCode = null;
        FailureMessage = null;
        PostedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string failureCode, string failureMessage)
    {
        FailureCode = WmsText.Required(failureCode, nameof(failureCode));
        FailureMessage = WmsText.Required(failureMessage, nameof(failureMessage));
        Status = InventoryMovementRequestStatus.Failed;
    }
}
