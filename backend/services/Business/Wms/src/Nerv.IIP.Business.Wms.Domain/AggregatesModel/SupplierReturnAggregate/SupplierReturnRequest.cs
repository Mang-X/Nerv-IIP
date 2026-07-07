namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;

public partial record SupplierReturnRequestId : IGuidStronglyTypedId;

public enum SupplierReturnRequestStatus
{
    Open = 0,
    Completed = 1,
    Cancelled = 2,
}

public sealed class SupplierReturnRequest : Entity<SupplierReturnRequestId>, IAggregateRoot
{
    public const string ReturnToSupplierDisposition = "return-to-supplier";

    private SupplierReturnRequest()
    {
    }

    private SupplierReturnRequest(
        string organizationId,
        string environmentId,
        string inboundOrderNo,
        string inboundOrderLineNo,
        string inspectionRecordId,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string ownerType,
        string? ownerId,
        decimal quantity,
        string? dispositionReason)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        InboundOrderNo = WmsText.Required(inboundOrderNo, nameof(inboundOrderNo));
        InboundOrderLineNo = WmsText.Required(inboundOrderLineNo, nameof(inboundOrderLineNo));
        InspectionRecordId = WmsText.Required(inspectionRecordId, nameof(inspectionRecordId));
        SupplierReturnNo = $"RTS-{InboundOrderNo}-{InboundOrderLineNo}-{InspectionRecordId}";
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        LocationCode = WmsText.Required(locationCode, nameof(locationCode));
        LotNo = WmsText.Optional(lotNo);
        SerialNo = WmsText.Optional(serialNo);
        OwnerType = WmsText.Required(ownerType, nameof(ownerType)).ToLowerInvariant();
        OwnerId = WmsText.Optional(ownerId);
        Quantity = WmsText.Positive(quantity, nameof(quantity));
        DispositionType = ReturnToSupplierDisposition;
        DispositionReason = WmsText.Optional(dispositionReason);
        Status = SupplierReturnRequestStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SupplierReturnNo { get; private set; } = string.Empty;
    public string InboundOrderNo { get; private set; } = string.Empty;
    public string InboundOrderLineNo { get; private set; } = string.Empty;
    public string InspectionRecordId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public decimal Quantity { get; private set; }
    public string DispositionType { get; private set; } = ReturnToSupplierDisposition;
    public string? DispositionReason { get; private set; }
    public SupplierReturnRequestStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SupplierReturnRequest Create(
        string organizationId,
        string environmentId,
        string inboundOrderNo,
        string inboundOrderLineNo,
        string inspectionRecordId,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        string ownerType,
        string? ownerId,
        decimal quantity,
        string? dispositionReason)
    {
        return new SupplierReturnRequest(
            organizationId,
            environmentId,
            inboundOrderNo,
            inboundOrderLineNo,
            inspectionRecordId,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            ownerType,
            ownerId,
            quantity,
            dispositionReason);
    }
}
