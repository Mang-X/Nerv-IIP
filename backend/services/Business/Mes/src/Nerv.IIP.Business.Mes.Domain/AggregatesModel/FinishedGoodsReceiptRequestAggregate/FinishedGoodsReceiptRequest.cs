namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;

public partial record FinishedGoodsReceiptRequestId : IGuidStronglyTypedId;

public sealed class FinishedGoodsReceiptRequest : Entity<FinishedGoodsReceiptRequestId>, IAggregateRoot
{
    private FinishedGoodsReceiptRequest()
    {
    }

    private FinishedGoodsReceiptRequest(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        decimal quantity,
        string uomCode,
        DateTimeOffset requestedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        SkuId = DomainGuard.Required(skuId, nameof(skuId));
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        UomCode = DomainGuard.Required(uomCode, nameof(uomCode));
        RequestedAtUtc = requestedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SkuId { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }

    public static FinishedGoodsReceiptRequest Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        decimal quantity,
        string uomCode,
        DateTimeOffset requestedAtUtc)
    {
        return new FinishedGoodsReceiptRequest(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            quantity,
            uomCode,
            requestedAtUtc);
    }

}
