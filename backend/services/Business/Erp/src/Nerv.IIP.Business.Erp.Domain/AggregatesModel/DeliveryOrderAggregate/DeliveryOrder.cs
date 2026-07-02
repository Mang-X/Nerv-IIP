using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;

public partial record DeliveryOrderId : IGuidStronglyTypedId;
public partial record DeliveryOrderLineId : IGuidStronglyTypedId;

public sealed record DeliveryOrderLineDraft(
    string SalesOrderLineNo,
    decimal Quantity,
    string? LocationCode = null,
    string? LotNo = null);

public sealed class DeliveryOrder : Entity<DeliveryOrderId>, IAggregateRoot
{
    private readonly List<DeliveryOrderLine> lines = [];

    private DeliveryOrder()
    {
    }

    private DeliveryOrder(SalesOrder order, string deliveryOrderNo, IEnumerable<DeliveryOrderLineDraft> lineDrafts)
    {
        OrganizationId = order.OrganizationId;
        EnvironmentId = order.EnvironmentId;
        DeliveryOrderNo = ErpText.Required(deliveryOrderNo, nameof(deliveryOrderNo));
        SalesOrderNo = order.SalesOrderNo;
        CustomerCode = order.CustomerCode;
        Status = "released";
        ReleasedAtUtc = DateTime.UtcNow;
        foreach (var draft in lineDrafts)
        {
            var orderLine = order.RegisterDelivery(draft.SalesOrderLineNo, draft.Quantity);
            lines.Add(DeliveryOrderLine.Create(draft, orderLine));
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one delivery line is required.", nameof(lineDrafts));
        }

        this.AddDomainEvent(new DeliveryOrderReleasedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeliveryOrderNo { get; private set; } = string.Empty;
    public string SalesOrderNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = "released";
    public DateTime ReleasedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public IReadOnlyCollection<DeliveryOrderLine> Lines => lines;

    public static DeliveryOrder Release(SalesOrder order, string deliveryOrderNo, IEnumerable<DeliveryOrderLineDraft> lines)
    {
        return new DeliveryOrder(order, deliveryOrderNo, lines);
    }

    public void Cancel(string reason, DateTime cancelledAtUtc)
    {
        if (string.Equals(Status, "cancelled", StringComparison.Ordinal))
        {
            return;
        }

        Status = "cancelled";
        CancelledAtUtc = cancelledAtUtc;
        CancellationReason = ErpText.Required(reason, nameof(reason));
    }
}

public sealed class DeliveryOrderLine : Entity<DeliveryOrderLineId>
{
    private DeliveryOrderLine()
    {
    }

    private DeliveryOrderLine(DeliveryOrderLineDraft draft)
    {
        SalesOrderLineNo = ErpText.Required(draft.SalesOrderLineNo, nameof(draft.SalesOrderLineNo));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        SkuCode = string.Empty;
        UomCode = string.Empty;
        LocationCode = string.IsNullOrWhiteSpace(draft.LocationCode) ? string.Empty : draft.LocationCode.Trim();
        LotNo = string.IsNullOrWhiteSpace(draft.LotNo) ? null : draft.LotNo.Trim();
    }

    private DeliveryOrderLine(DeliveryOrderLineDraft draft, SalesOrderLine orderLine)
    {
        SalesOrderLineNo = ErpText.Required(draft.SalesOrderLineNo, nameof(draft.SalesOrderLineNo));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        SkuCode = orderLine.SkuCode;
        UomCode = orderLine.UomCode;
        LocationCode = string.IsNullOrWhiteSpace(draft.LocationCode) ? "default" : draft.LocationCode.Trim();
        LotNo = string.IsNullOrWhiteSpace(draft.LotNo) ? null : draft.LotNo.Trim();
    }

    public string SalesOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public decimal Quantity { get; private set; }

    public static DeliveryOrderLine Create(DeliveryOrderLineDraft draft)
    {
        return new DeliveryOrderLine(draft);
    }

    public static DeliveryOrderLine Create(DeliveryOrderLineDraft draft, SalesOrderLine orderLine)
    {
        return new DeliveryOrderLine(draft, orderLine);
    }
}
