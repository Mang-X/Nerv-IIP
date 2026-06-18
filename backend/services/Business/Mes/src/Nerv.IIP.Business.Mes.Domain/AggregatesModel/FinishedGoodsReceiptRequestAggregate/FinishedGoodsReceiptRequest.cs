using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;

public partial record FinishedGoodsReceiptRequestId : IGuidStronglyTypedId;

public sealed class FinishedGoodsReceiptRequest : Entity<FinishedGoodsReceiptRequestId>, IAggregateRoot
{
    public const string RequestedStatus = "Requested";
    public const string PostedStatus = "Posted";

    private FinishedGoodsReceiptRequest()
    {
    }

    private FinishedGoodsReceiptRequest(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string skuId,
        decimal quantity,
        string uomCode,
        DateTimeOffset requestedAtUtc,
        string? producedLotNo,
        string? serialNo)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        RequestNo = DomainGuard.Required(requestNo, nameof(requestNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        SkuId = DomainGuard.Required(skuId, nameof(skuId));
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        UomCode = DomainGuard.Required(uomCode, nameof(uomCode));
        RequestedAtUtc = requestedAtUtc;
        ProducedLotNo = string.IsNullOrWhiteSpace(producedLotNo) ? null : producedLotNo.Trim();
        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
        Status = RequestedStatus;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RequestNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SkuId { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public string? ProducedLotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? PostedInventoryMovementId { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }

    public static FinishedGoodsReceiptRequest Create(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string skuId,
        decimal quantity,
        string uomCode,
        DateTimeOffset requestedAtUtc,
        string? producedLotNo = null,
        string? serialNo = null)
    {
        var request = new FinishedGoodsReceiptRequest(
            organizationId,
            environmentId,
            requestNo,
            workOrderId,
            skuId,
            quantity,
            uomCode,
            requestedAtUtc,
            producedLotNo,
            serialNo);
        request.AddDomainEvent(new FinishedGoodsReceiptRequestedDomainEvent(request));
        return request;
    }

    public void MarkPosted(string inventoryMovementId, DateTimeOffset postedAtUtc)
    {
        PostedInventoryMovementId = DomainGuard.Required(inventoryMovementId, nameof(inventoryMovementId));
        PostedAtUtc = postedAtUtc;
        Status = PostedStatus;
    }

}
