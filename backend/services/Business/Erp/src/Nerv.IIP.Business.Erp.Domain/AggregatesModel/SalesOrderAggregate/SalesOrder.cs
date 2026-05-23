using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;

public partial record SalesOrderId : IGuidStronglyTypedId;
public partial record SalesOrderLineId : IGuidStronglyTypedId;

public sealed class SalesOrder : Entity<SalesOrderId>, IAggregateRoot
{
    private readonly List<SalesOrderLine> lines = [];

    private SalesOrder()
    {
    }

    private SalesOrder(string salesOrderNo, Quotation quotation)
    {
        quotation.EnsureCanCreateSalesOrder(DateOnly.FromDateTime(DateTime.UtcNow));
        OrganizationId = quotation.OrganizationId;
        EnvironmentId = quotation.EnvironmentId;
        SalesOrderNo = ErpText.Required(salesOrderNo, nameof(salesOrderNo));
        QuotationNo = quotation.QuotationNo;
        CustomerCode = quotation.CustomerCode;
        Status = "released";
        CreatedAtUtc = DateTime.UtcNow;
        lines.AddRange(quotation.Lines.Select(line => SalesOrderLine.Create(line.LineNo, line.SkuCode, line.UomCode, line.Quantity, line.UnitPrice, line.RequiredDate)));
        TotalAmount = lines.Sum(x => x.LineAmount);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SalesOrderNo { get; private set; } = string.Empty;
    public string QuotationNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<SalesOrderLine> Lines => lines;

    public static SalesOrder CreateFromQuotation(string salesOrderNo, Quotation quotation)
    {
        return new SalesOrder(salesOrderNo, quotation);
    }

    public void RegisterDelivery(string lineNo, decimal quantity)
    {
        var line = lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Sales order line '{lineNo}' was not found.");
        line.RegisterDelivery(quantity);
    }
}

public sealed class SalesOrderLine : Entity<SalesOrderLineId>
{
    private SalesOrderLine()
    {
    }

    private SalesOrderLine(string lineNo, string skuCode, string uomCode, decimal quantity, decimal unitPrice, DateOnly requiredDate)
    {
        LineNo = ErpText.Required(lineNo, nameof(lineNo));
        SkuCode = ErpText.Required(skuCode, nameof(skuCode));
        UomCode = ErpText.Required(uomCode, nameof(uomCode));
        OrderedQuantity = ErpText.Positive(quantity, nameof(quantity));
        UnitPrice = ErpText.Positive(unitPrice, nameof(unitPrice));
        RequiredDate = requiredDate;
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public decimal DeliveredQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateOnly RequiredDate { get; private set; }
    public decimal OpenQuantity => OrderedQuantity - DeliveredQuantity;
    public decimal LineAmount => OrderedQuantity * UnitPrice;

    public static SalesOrderLine Create(string lineNo, string skuCode, string uomCode, decimal quantity, decimal unitPrice, DateOnly requiredDate)
    {
        return new SalesOrderLine(lineNo, skuCode, uomCode, quantity, unitPrice, requiredDate);
    }

    public void RegisterDelivery(decimal quantity)
    {
        _ = ErpText.Positive(quantity, nameof(quantity));
        if (quantity > OpenQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Delivery quantity cannot exceed open ordered quantity.");
        }

        DeliveredQuantity += quantity;
    }
}
