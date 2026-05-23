using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;

public partial record DeliveryOrderId : IGuidStronglyTypedId;
public partial record DeliveryOrderLineId : IGuidStronglyTypedId;

public sealed record DeliveryOrderLineDraft(string SalesOrderLineNo, decimal Quantity);

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
        ReleasedAtUtc = DateTime.UtcNow;
        foreach (var draft in lineDrafts)
        {
            order.RegisterDelivery(draft.SalesOrderLineNo, draft.Quantity);
            lines.Add(DeliveryOrderLine.Create(draft));
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
    public DateTime ReleasedAtUtc { get; private set; }
    public IReadOnlyCollection<DeliveryOrderLine> Lines => lines;

    public static DeliveryOrder Release(SalesOrder order, string deliveryOrderNo, IEnumerable<DeliveryOrderLineDraft> lines)
    {
        return new DeliveryOrder(order, deliveryOrderNo, lines);
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
    }

    public string SalesOrderLineNo { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    public static DeliveryOrderLine Create(DeliveryOrderLineDraft draft)
    {
        return new DeliveryOrderLine(draft);
    }
}
