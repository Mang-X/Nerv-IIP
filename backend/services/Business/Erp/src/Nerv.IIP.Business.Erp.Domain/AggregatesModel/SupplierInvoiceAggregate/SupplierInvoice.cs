using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;

public partial record SupplierInvoiceId : IGuidStronglyTypedId;
public partial record SupplierInvoiceLineId : IGuidStronglyTypedId;

public enum SupplierInvoiceMatchStatus
{
    Matched = 0,
    PaymentHeld = 1,
    Voided = 2,
}

public sealed record SupplierInvoiceLineDraft(
    string PurchaseOrderLineNo,
    string PurchaseReceiptLineNo,
    decimal InvoiceQuantity,
    decimal UnitPrice);

public sealed class SupplierInvoice : Entity<SupplierInvoiceId>, IAggregateRoot
{
    private readonly List<SupplierInvoiceLine> lines = [];

    private SupplierInvoice()
    {
    }

    private SupplierInvoice(
        PurchaseOrder order,
        PurchaseReceipt receipt,
        string invoiceNo,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string currencyCode,
        decimal quantityTolerance,
        decimal amountTolerance,
        IEnumerable<SupplierInvoiceLineDraft> lineDrafts,
        IReadOnlyDictionary<string, decimal>? alreadyInvoicedQuantitiesByReceiptLineNo)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!string.Equals(order.PurchaseOrderNo, receipt.PurchaseOrderNo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Supplier invoice receipt must reference the purchase order being matched.");
        }

        OrganizationId = order.OrganizationId;
        EnvironmentId = order.EnvironmentId;
        InvoiceNo = ErpText.Required(invoiceNo, nameof(invoiceNo));
        PurchaseOrderNo = order.PurchaseOrderNo;
        PurchaseReceiptNo = receipt.PurchaseReceiptNo;
        SupplierCode = order.SupplierCode;
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        MatchedAtUtc = DateTime.UtcNow;

        var held = false;
        var currentInvoiceQuantitiesByReceiptLineNo = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var draft in lineDrafts)
        {
            var poLine = order.Lines.SingleOrDefault(x => x.LineNo == draft.PurchaseOrderLineNo)
                ?? throw new InvalidOperationException($"Purchase order line '{draft.PurchaseOrderLineNo}' was not found.");
            var receiptLine = receipt.Lines.SingleOrDefault(x => x.PurchaseOrderLineNo == draft.PurchaseReceiptLineNo)
                ?? throw new InvalidOperationException($"Purchase receipt line '{draft.PurchaseReceiptLineNo}' was not found.");
            if (!string.Equals(receiptLine.PurchaseOrderLineNo, poLine.LineNo, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Supplier invoice purchase order line and receipt line do not align.");
            }

            var alreadyInvoicedQuantity = alreadyInvoicedQuantitiesByReceiptLineNo?.GetValueOrDefault(draft.PurchaseReceiptLineNo) ?? 0m;
            var currentInvoiceQuantity = currentInvoiceQuantitiesByReceiptLineNo.GetValueOrDefault(draft.PurchaseReceiptLineNo);
            if (!IsWithinTolerance(draft, poLine, receiptLine, alreadyInvoicedQuantity + currentInvoiceQuantity, quantityTolerance, amountTolerance))
            {
                held = true;
            }

            lines.Add(SupplierInvoiceLine.Create(draft, poLine));
            currentInvoiceQuantitiesByReceiptLineNo[draft.PurchaseReceiptLineNo] = currentInvoiceQuantity + draft.InvoiceQuantity;
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one supplier invoice line is required.", nameof(lineDrafts));
        }

        TotalAmount = lines.Sum(x => x.LineAmount);
        MatchStatus = held ? SupplierInvoiceMatchStatus.PaymentHeld : SupplierInvoiceMatchStatus.Matched;
        if (MatchStatus == SupplierInvoiceMatchStatus.Matched)
        {
            this.AddDomainEvent(new SupplierInvoiceMatchedDomainEvent(this));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string InvoiceNo { get; private set; } = string.Empty;
    public string PurchaseOrderNo { get; private set; } = string.Empty;
    public string PurchaseReceiptNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal TotalAmount { get; private set; }
    public SupplierInvoiceMatchStatus MatchStatus { get; private set; }
    public DateTime MatchedAtUtc { get; private set; }
    public IReadOnlyCollection<SupplierInvoiceLine> Lines => lines;

    public static SupplierInvoice Match(
        PurchaseOrder order,
        PurchaseReceipt receipt,
        string invoiceNo,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string currencyCode,
        decimal quantityTolerance,
        decimal amountTolerance,
        IEnumerable<SupplierInvoiceLineDraft> lines,
        IReadOnlyDictionary<string, decimal>? alreadyInvoicedQuantitiesByReceiptLineNo = null)
    {
        return new SupplierInvoice(order, receipt, invoiceNo, invoiceDate, dueDate, currencyCode, quantityTolerance, amountTolerance, lines, alreadyInvoicedQuantitiesByReceiptLineNo);
    }

    public void ReleasePaymentHold()
    {
        if (MatchStatus == SupplierInvoiceMatchStatus.Matched)
        {
            return;
        }

        if (MatchStatus != SupplierInvoiceMatchStatus.PaymentHeld)
        {
            throw new InvalidOperationException("Only held supplier invoices can be released.");
        }

        MatchStatus = SupplierInvoiceMatchStatus.Matched;
        this.AddDomainEvent(new SupplierInvoiceMatchedDomainEvent(this));
    }

    public void VoidPaymentHold()
    {
        if (MatchStatus == SupplierInvoiceMatchStatus.Voided)
        {
            return;
        }

        if (MatchStatus != SupplierInvoiceMatchStatus.PaymentHeld)
        {
            throw new InvalidOperationException("Only held supplier invoices can be voided.");
        }

        MatchStatus = SupplierInvoiceMatchStatus.Voided;
    }

    private static bool IsWithinTolerance(
        SupplierInvoiceLineDraft draft,
        PurchaseOrderLine poLine,
        PurchaseReceiptLine receiptLine,
        decimal alreadyInvoicedQuantity,
        decimal quantityTolerance,
        decimal amountTolerance)
    {
        var invoiceQuantity = ErpText.Positive(draft.InvoiceQuantity, nameof(draft.InvoiceQuantity));
        var unitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        if (alreadyInvoicedQuantity + invoiceQuantity > receiptLine.ReceivedQuantity + quantityTolerance)
        {
            return false;
        }

        var priceDeltaAmount = Math.Abs(unitPrice - poLine.UnitPrice) * invoiceQuantity;
        return priceDeltaAmount <= amountTolerance;
    }
}

public sealed class SupplierInvoiceLine : Entity<SupplierInvoiceLineId>
{
    private SupplierInvoiceLine()
    {
    }

    private SupplierInvoiceLine(SupplierInvoiceLineDraft draft, PurchaseOrderLine poLine)
    {
        PurchaseOrderLineNo = ErpText.Required(draft.PurchaseOrderLineNo, nameof(draft.PurchaseOrderLineNo));
        PurchaseReceiptLineNo = ErpText.Required(draft.PurchaseReceiptLineNo, nameof(draft.PurchaseReceiptLineNo));
        SkuCode = poLine.SkuCode;
        UomCode = poLine.UomCode;
        InvoiceQuantity = ErpText.Positive(draft.InvoiceQuantity, nameof(draft.InvoiceQuantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
    }

    public string PurchaseOrderLineNo { get; private set; } = string.Empty;
    public string PurchaseReceiptLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal InvoiceQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineAmount => InvoiceQuantity * UnitPrice;

    public static SupplierInvoiceLine Create(SupplierInvoiceLineDraft draft, PurchaseOrderLine poLine)
    {
        return new SupplierInvoiceLine(draft, poLine);
    }
}
