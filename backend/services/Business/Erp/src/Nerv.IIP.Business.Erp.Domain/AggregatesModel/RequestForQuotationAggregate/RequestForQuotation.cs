using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;

public partial record RequestForQuotationId : IGuidStronglyTypedId;
public partial record RfqLineId : IGuidStronglyTypedId;

public enum RequestForQuotationStatus
{
    Open = 0,
    Closed = 1,
    Cancelled = 2,
}

public sealed record RfqLineDraft(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string SiteCode,
    DateOnly RequiredDate);

public sealed class RequestForQuotation : Entity<RequestForQuotationId>, IAggregateRoot
{
    private readonly List<RfqLine> lines = [];
    private readonly List<RfqSupplier> suppliers = [];

    private RequestForQuotation()
    {
    }

    private RequestForQuotation(
        string organizationId,
        string environmentId,
        string rfqNo,
        IEnumerable<string> supplierCodes,
        IEnumerable<RfqLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        RfqNo = ErpText.Required(rfqNo, nameof(rfqNo));
        Status = RequestForQuotationStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
        suppliers.AddRange(supplierCodes.Select(RfqSupplier.Create));
        lines.AddRange(lineDrafts.Select(RfqLine.Create));
        if (suppliers.Count == 0)
        {
            throw new ArgumentException("At least one RFQ supplier is required.", nameof(supplierCodes));
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one RFQ line is required.", nameof(lineDrafts));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RfqNo { get; private set; } = string.Empty;
    public RequestForQuotationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<RfqLine> Lines => lines;
    public IReadOnlyCollection<RfqSupplier> Suppliers => suppliers;

    public static RequestForQuotation Create(
        string organizationId,
        string environmentId,
        string rfqNo,
        IEnumerable<string> supplierCodes,
        IEnumerable<RfqLineDraft> lines)
    {
        return new RequestForQuotation(organizationId, environmentId, rfqNo, supplierCodes, lines);
    }
}

public sealed class RfqLine : Entity<RfqLineId>
{
    private RfqLine()
    {
    }

    private RfqLine(RfqLineDraft draft)
    {
        LineNo = ErpText.Required(draft.LineNo, nameof(draft.LineNo));
        SkuCode = ErpText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = ErpText.Required(draft.UomCode, nameof(draft.UomCode));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        SiteCode = ErpText.Required(draft.SiteCode, nameof(draft.SiteCode));
        RequiredDate = draft.RequiredDate;
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string SiteCode { get; private set; } = string.Empty;
    public DateOnly RequiredDate { get; private set; }

    public static RfqLine Create(RfqLineDraft draft)
    {
        return new RfqLine(draft);
    }
}

public sealed class RfqSupplier
{
    private RfqSupplier()
    {
    }

    private RfqSupplier(string supplierCode)
    {
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
    }

    public long Id { get; private set; }
    public string SupplierCode { get; private set; } = string.Empty;

    public static RfqSupplier Create(string supplierCode)
    {
        return new RfqSupplier(supplierCode);
    }
}
