using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;

public partial record PurchaseReturnId : IGuidStronglyTypedId;
public partial record PurchaseReturnLineId : IGuidStronglyTypedId;

public sealed record PurchaseReturnLineDraft(
    string PurchaseOrderLineNo,
    string SkuCode,
    string UomCode,
    decimal ReturnedQuantity,
    decimal UnitPrice,
    decimal GrIrReversalQuantity,
    decimal DebitNoteQuantity);

public sealed class PurchaseReturn : Entity<PurchaseReturnId>, IAggregateRoot
{
    private readonly List<PurchaseReturnLine> lines = [];

    private PurchaseReturn()
    {
    }

    private PurchaseReturn(
        string organizationId,
        string environmentId,
        string purchaseReturnNo,
        string purchaseReceiptNo,
        string wmsOutboundOrderNo,
        string supplierCode,
        string currencyCode,
        decimal exchangeRate,
        IEnumerable<PurchaseReturnLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        PurchaseReturnNo = ErpText.Required(purchaseReturnNo, nameof(purchaseReturnNo));
        PurchaseReceiptNo = ErpText.Required(purchaseReceiptNo, nameof(purchaseReceiptNo));
        WmsOutboundOrderNo = ErpText.Required(wmsOutboundOrderNo, nameof(wmsOutboundOrderNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ExchangeRate = ErpText.Positive(exchangeRate, nameof(exchangeRate));
        lines.AddRange(lineDrafts.Select(PurchaseReturnLine.Create));
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one purchase return line is required.", nameof(lineDrafts));
        }

        RecordedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PurchaseReturnNo { get; private set; } = string.Empty;
    public string PurchaseReceiptNo { get; private set; } = string.Empty;
    public string WmsOutboundOrderNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public IReadOnlyCollection<PurchaseReturnLine> Lines => lines;
    public decimal GrIrReversalAmount => lines.Sum(x => x.GrIrReversalAmount);
    public decimal DebitNoteAmount => lines.Sum(x => x.DebitNoteAmount);
    public decimal TotalAmount => GrIrReversalAmount + DebitNoteAmount;

    public static PurchaseReturn Record(
        string organizationId,
        string environmentId,
        string purchaseReturnNo,
        string purchaseReceiptNo,
        string wmsOutboundOrderNo,
        string supplierCode,
        string currencyCode,
        decimal exchangeRate,
        IEnumerable<PurchaseReturnLineDraft> lines)
        => new(organizationId, environmentId, purchaseReturnNo, purchaseReceiptNo, wmsOutboundOrderNo, supplierCode, currencyCode, exchangeRate, lines);
}

public sealed class PurchaseReturnLine : Entity<PurchaseReturnLineId>
{
    private PurchaseReturnLine()
    {
    }

    private PurchaseReturnLine(PurchaseReturnLineDraft draft)
    {
        PurchaseOrderLineNo = ErpText.Required(draft.PurchaseOrderLineNo, nameof(draft.PurchaseOrderLineNo));
        SkuCode = ErpText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = ErpText.Required(draft.UomCode, nameof(draft.UomCode));
        ReturnedQuantity = ErpText.Positive(draft.ReturnedQuantity, nameof(draft.ReturnedQuantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        if (draft.GrIrReversalQuantity < 0m || draft.DebitNoteQuantity < 0m || draft.GrIrReversalQuantity + draft.DebitNoteQuantity != ReturnedQuantity)
        {
            throw new ArgumentException("Purchase return line compensation quantities must exactly partition the returned quantity.", nameof(draft));
        }

        GrIrReversalQuantity = draft.GrIrReversalQuantity;
        DebitNoteQuantity = draft.DebitNoteQuantity;
    }

    public string PurchaseOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal ReturnedQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal GrIrReversalQuantity { get; private set; }
    public decimal DebitNoteQuantity { get; private set; }
    public decimal GrIrReversalAmount => GrIrReversalQuantity * UnitPrice;
    public decimal DebitNoteAmount => DebitNoteQuantity * UnitPrice;

    public static PurchaseReturnLine Create(PurchaseReturnLineDraft draft) => new(draft);
}
