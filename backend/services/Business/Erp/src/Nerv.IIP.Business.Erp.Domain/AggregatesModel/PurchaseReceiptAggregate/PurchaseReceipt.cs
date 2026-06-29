using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;

public partial record PurchaseReceiptId : IGuidStronglyTypedId;
public partial record PurchaseReceiptLineId : IGuidStronglyTypedId;

public enum PurchaseReceiptStatus
{
    Recorded = 0,
}

public sealed record PurchaseReceiptLineDraft(
    string PurchaseOrderLineNo,
    decimal ReceivedQuantity,
    string QualityStatus,
    string? LocationCode = null,
    string? LotNo = null,
    bool FinalDelivery = false);

public sealed class PurchaseReceipt : Entity<PurchaseReceiptId>, IAggregateRoot
{
    private readonly List<PurchaseReceiptLine> lines = [];

    private PurchaseReceipt()
    {
    }

    private PurchaseReceipt(PurchaseOrder order, string purchaseReceiptNo, IEnumerable<PurchaseReceiptLineDraft> lineDrafts, decimal exchangeRate)
    {
        ArgumentNullException.ThrowIfNull(order);
        OrganizationId = order.OrganizationId;
        EnvironmentId = order.EnvironmentId;
        PurchaseReceiptNo = ErpText.Required(purchaseReceiptNo, nameof(purchaseReceiptNo));
        PurchaseOrderNo = order.PurchaseOrderNo;
        SupplierCode = order.SupplierCode;
        SiteCode = order.SiteCode;
        CurrencyCode = order.CurrencyCode;
        ExchangeRate = ErpText.Positive(exchangeRate, nameof(exchangeRate));
        Status = PurchaseReceiptStatus.Recorded;
        RecordedAtUtc = DateTime.UtcNow;
        foreach (var draft in lineDrafts)
        {
            var orderLine = order.RegisterReceipt(draft.PurchaseOrderLineNo, draft.ReceivedQuantity, draft.FinalDelivery);
            var line = PurchaseReceiptLine.Create(draft, orderLine, SiteCode);
            lines.Add(line);
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one purchase receipt line is required.", nameof(lineDrafts));
        }

        var qualityStatuses = lines.Select(x => x.QualityStatus).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        QualityStatus = qualityStatuses.Length == 1 ? qualityStatuses[0] : "mixed";
        this.AddDomainEvent(new PurchaseReceiptRecordedDomainEvent(this));
        foreach (var line in lines)
        {
            this.AddDomainEvent(new PurchaseReceiptInventoryMovementRequestedDomainEvent(this, line));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PurchaseReceiptNo { get; private set; } = string.Empty;
    public string PurchaseOrderNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public PurchaseReceiptStatus Status { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public IReadOnlyCollection<PurchaseReceiptLine> Lines => lines;

    public static PurchaseReceipt Record(PurchaseOrder order, string purchaseReceiptNo, IEnumerable<PurchaseReceiptLineDraft> lines, decimal exchangeRate = 1m)
    {
        return new PurchaseReceipt(order, purchaseReceiptNo, lines, exchangeRate);
    }

    public void Cancel()
    {
        throw new InvalidOperationException("Recorded purchase receipts are immutable.");
    }
}

public sealed class PurchaseReceiptLine : Entity<PurchaseReceiptLineId>
{
    private PurchaseReceiptLine()
    {
    }

    private PurchaseReceiptLine(PurchaseReceiptLineDraft draft)
    {
        PurchaseOrderLineNo = ErpText.Required(draft.PurchaseOrderLineNo, nameof(draft.PurchaseOrderLineNo));
        ReceivedQuantity = ErpText.Positive(draft.ReceivedQuantity, nameof(draft.ReceivedQuantity));
        QualityStatus = ErpText.Required(draft.QualityStatus, nameof(draft.QualityStatus)).ToLowerInvariant();
        SkuCode = string.Empty;
        UomCode = string.Empty;
        LocationCode = string.Empty;
        LotNo = draft.LotNo;
    }

    private PurchaseReceiptLine(PurchaseReceiptLineDraft draft, PurchaseOrderLine orderLine, string siteCode)
    {
        PurchaseOrderLineNo = ErpText.Required(draft.PurchaseOrderLineNo, nameof(draft.PurchaseOrderLineNo));
        ReceivedQuantity = ErpText.Positive(draft.ReceivedQuantity, nameof(draft.ReceivedQuantity));
        QualityStatus = ErpText.Required(draft.QualityStatus, nameof(draft.QualityStatus)).ToLowerInvariant();
        SkuCode = orderLine.SkuCode;
        UomCode = orderLine.UomCode;
        LocationCode = string.IsNullOrWhiteSpace(draft.LocationCode) ? siteCode : draft.LocationCode.Trim();
        LotNo = string.IsNullOrWhiteSpace(draft.LotNo) ? null : draft.LotNo.Trim();
    }

    public string PurchaseOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;

    public static PurchaseReceiptLine Create(PurchaseReceiptLineDraft draft)
    {
        return new PurchaseReceiptLine(draft);
    }

    public static PurchaseReceiptLine Create(PurchaseReceiptLineDraft draft, PurchaseOrderLine orderLine, string siteCode)
    {
        return new PurchaseReceiptLine(draft, orderLine, siteCode);
    }
}
