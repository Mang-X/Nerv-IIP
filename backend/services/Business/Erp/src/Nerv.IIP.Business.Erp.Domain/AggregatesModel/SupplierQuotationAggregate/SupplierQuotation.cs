using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;

public partial record SupplierQuotationId : IGuidStronglyTypedId;
public partial record SupplierQuotationLineId : IGuidStronglyTypedId;

public sealed record SupplierQuotationLineDraft(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

public sealed class SupplierQuotation : Entity<SupplierQuotationId>, IAggregateRoot
{
    private readonly List<SupplierQuotationLine> lines = [];

    private SupplierQuotation()
    {
    }

    private SupplierQuotation(
        string organizationId,
        string environmentId,
        string quotationNo,
        string rfqNo,
        string supplierCode,
        IEnumerable<SupplierQuotationLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        QuotationNo = ErpText.Required(quotationNo, nameof(quotationNo));
        RfqNo = ErpText.Required(rfqNo, nameof(rfqNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        ReceivedAtUtc = DateTime.UtcNow;
        lines.AddRange(lineDrafts.Select(SupplierQuotationLine.Create));
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one supplier quotation line is required.", nameof(lineDrafts));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string QuotationNo { get; private set; } = string.Empty;
    public string RfqNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public IReadOnlyCollection<SupplierQuotationLine> Lines => lines;

    public static SupplierQuotation Receive(
        string organizationId,
        string environmentId,
        string quotationNo,
        string rfqNo,
        string supplierCode,
        IEnumerable<SupplierQuotationLineDraft> lines)
    {
        return new SupplierQuotation(organizationId, environmentId, quotationNo, rfqNo, supplierCode, lines);
    }
}

public sealed class SupplierQuotationLine : Entity<SupplierQuotationLineId>
{
    private SupplierQuotationLine()
    {
    }

    private SupplierQuotationLine(SupplierQuotationLineDraft draft)
    {
        LineNo = ErpText.Required(draft.LineNo, nameof(draft.LineNo));
        SkuCode = ErpText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = ErpText.Required(draft.UomCode, nameof(draft.UomCode));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        PromisedDate = draft.PromisedDate;
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateOnly PromisedDate { get; private set; }

    public static SupplierQuotationLine Create(SupplierQuotationLineDraft draft)
    {
        return new SupplierQuotationLine(draft);
    }
}
