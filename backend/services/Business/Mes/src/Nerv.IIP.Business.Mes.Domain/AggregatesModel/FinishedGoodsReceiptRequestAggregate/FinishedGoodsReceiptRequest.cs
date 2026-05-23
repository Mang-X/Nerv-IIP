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
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        WorkOrderId = Required(workOrderId);
        SkuId = Required(skuId);
        Quantity = quantity > 0 ? quantity : throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        UomCode = Required(uomCode);
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

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
