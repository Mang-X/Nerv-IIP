using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;

public partial record SalesOrderId : IGuidStronglyTypedId;
public partial record SalesOrderLineId : IGuidStronglyTypedId;

public sealed record CustomerCreditSnapshot(
    string CustomerCode,
    decimal CreditLimit,
    decimal OpenReceivableAmount,
    decimal ActiveSalesOrderExposure);

public sealed class SalesOrder : Entity<SalesOrderId>, IAggregateRoot
{
    private readonly List<SalesOrderLine> lines = [];
    private readonly List<SalesOrderChange> changeHistory = [];

    private SalesOrder()
    {
    }

    private SalesOrder(string salesOrderNo, Quotation quotation, CustomerCreditSnapshot? creditSnapshot)
    {
        quotation.EnsureCanCreateSalesOrder(DateOnly.FromDateTime(DateTime.UtcNow));
        OrganizationId = quotation.OrganizationId;
        EnvironmentId = quotation.EnvironmentId;
        SalesOrderNo = ErpText.Required(salesOrderNo, nameof(salesOrderNo));
        QuotationNo = quotation.QuotationNo;
        CustomerCode = quotation.CustomerCode;
        Status = "released";
        Version = 1;
        CreatedAtUtc = DateTime.UtcNow;
        lines.AddRange(quotation.Lines.Select(line => SalesOrderLine.Create(line.LineNo, line.SkuCode, line.UomCode, line.Quantity, line.UnitPrice, line.RequiredDate)));
        TotalAmount = lines.Sum(x => x.LineAmount);
        ApplyCreditStatus(creditSnapshot);
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
    public int Version { get; private set; }
    public IReadOnlyCollection<SalesOrderChange> ChangeHistory => changeHistory;
    public decimal OpenExposureAmount => lines.Sum(x => x.OpenQuantity * x.UnitPrice);

    public static SalesOrder CreateFromQuotation(string salesOrderNo, Quotation quotation)
    {
        return new SalesOrder(salesOrderNo, quotation, null);
    }

    public static SalesOrder CreateFromQuotation(string salesOrderNo, Quotation quotation, CustomerCreditSnapshot creditSnapshot)
    {
        return new SalesOrder(salesOrderNo, quotation, creditSnapshot);
    }

    public SalesOrderLine RegisterDelivery(string lineNo, decimal quantity)
    {
        if (!string.Equals(Status, "released", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only released sales orders can be delivered.");
        }

        var line = lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Sales order line '{lineNo}' was not found.");
        line.RegisterDelivery(quantity);
        return line;
    }

    public void ReleaseDelivery(string lineNo, decimal quantity)
    {
        var line = FindLine(lineNo);
        line.ReleaseDelivery(quantity);
    }

    public void ReleaseCreditHold()
    {
        if (Status == "released")
        {
            return;
        }

        if (Status != "credit-held")
        {
            throw new InvalidOperationException("Only credit-held sales orders can be released.");
        }

        Status = "released";
    }

    public void ChangeLine(string lineNo, decimal orderedQuantity, decimal unitPrice, DateOnly requiredDate, string reason)
    {
        EnsureChangeable();
        var line = FindLine(lineNo);
        line.Change(orderedQuantity, unitPrice, requiredDate);
        changeHistory.Add(SalesOrderChange.Create("amend", line.LineNo, reason));
        RecalculateTotalAmount();
        Version++;
    }

    public void CancelLine(string lineNo, string reason)
    {
        EnsureChangeable();
        var line = FindLine(lineNo);
        line.Cancel();
        changeHistory.Add(SalesOrderChange.Create("cancel-line", line.LineNo, reason));
        RecalculateTotalAmount();
        Version++;
        if (lines.All(x => x.Cancelled))
        {
            Status = "cancelled";
        }
    }

    public void Cancel(string reason)
    {
        EnsureChangeable();
        if (lines.Any(x => x.DeliveredQuantity > 0m))
        {
            throw new InvalidOperationException("Sales orders with delivered quantity cannot be cancelled.");
        }

        foreach (var line in lines)
        {
            line.Cancel();
        }

        RecalculateTotalAmount();
        changeHistory.Add(SalesOrderChange.Create("cancel", null, reason));
        Version++;
        Status = "cancelled";
    }

    private SalesOrderLine FindLine(string lineNo)
    {
        return lines.SingleOrDefault(x => x.LineNo == ErpText.Required(lineNo, nameof(lineNo)))
            ?? throw new InvalidOperationException($"Sales order line '{lineNo}' was not found.");
    }

    private void RecalculateTotalAmount()
    {
        TotalAmount = lines
            .Where(line => !line.Cancelled)
            .Sum(line => line.LineAmount);
    }

    private void EnsureChangeable()
    {
        if (!string.Equals(Status, "released", StringComparison.Ordinal) && !string.Equals(Status, "credit-held", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only open sales orders can be changed or cancelled.");
        }
    }

    private void ApplyCreditStatus(CustomerCreditSnapshot? creditSnapshot)
    {
        if (creditSnapshot is null)
        {
            return;
        }

        if (!string.Equals(CustomerCode, creditSnapshot.CustomerCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Credit snapshot customer does not match the sales order customer.");
        }

        var exposureAfterOrder = creditSnapshot.OpenReceivableAmount + creditSnapshot.ActiveSalesOrderExposure + TotalAmount;
        if (exposureAfterOrder > creditSnapshot.CreditLimit)
        {
            Status = "credit-held";
        }
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
    public bool Cancelled { get; private set; }
    public decimal OpenQuantity => Cancelled ? 0m : OrderedQuantity - DeliveredQuantity;
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

    internal void ReleaseDelivery(decimal quantity)
    {
        _ = ErpText.Positive(quantity, nameof(quantity));
        if (quantity > DeliveredQuantity)
        {
            throw new InvalidOperationException("Released delivery quantity cannot exceed the sales order line delivery quantity.");
        }

        DeliveredQuantity -= quantity;
    }

    internal void Change(decimal orderedQuantity, decimal unitPrice, DateOnly requiredDate)
    {
        if (Cancelled || DeliveredQuantity > 0m)
        {
            throw new InvalidOperationException("Only unfulfilled sales order lines can be changed.");
        }

        OrderedQuantity = ErpText.Positive(orderedQuantity, nameof(orderedQuantity));
        UnitPrice = ErpText.Positive(unitPrice, nameof(unitPrice));
        RequiredDate = requiredDate;
    }

    internal void Cancel()
    {
        if (Cancelled)
        {
            return;
        }

        if (DeliveredQuantity > 0m)
        {
            throw new InvalidOperationException("Delivered sales order lines cannot be cancelled.");
        }

        Cancelled = true;
    }
}

public sealed class SalesOrderChange
{
    private SalesOrderChange()
    {
    }

    private SalesOrderChange(string changeType, string? lineNo, string reason)
    {
        ChangeType = ErpText.Required(changeType, nameof(changeType));
        LineNo = ErpText.Optional(lineNo);
        Reason = ErpText.Required(reason, nameof(reason));
        ChangedAtUtc = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public string ChangeType { get; private set; } = string.Empty;
    public string? LineNo { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime ChangedAtUtc { get; private set; }

    public static SalesOrderChange Create(string changeType, string? lineNo, string reason) => new(changeType, lineNo, reason);
}
