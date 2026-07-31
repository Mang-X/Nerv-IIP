using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;

public partial record QuotationId : IGuidStronglyTypedId;
public partial record QuotationLineId : IGuidStronglyTypedId;

public enum QuotationStatus
{
    Draft = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3,
}

public sealed record QuotationLineDraft(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly RequiredDate);

public sealed class Quotation : Entity<QuotationId>, IAggregateRoot
{
    private readonly List<QuotationLine> lines = [];

    private Quotation()
    {
    }

    private Quotation(
        string organizationId,
        string environmentId,
        string quotationNo,
        string customerCode,
        DateOnly expiresOn,
        IEnumerable<QuotationLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        QuotationNo = ErpText.Required(quotationNo, nameof(quotationNo));
        CustomerCode = ErpText.Required(customerCode, nameof(customerCode));
        ExpiresOn = expiresOn;
        Status = QuotationStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        lines.AddRange(lineDrafts.Select(QuotationLine.Create));
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one quotation line is required.", nameof(lineDrafts));
        }

        TotalAmount = lines.Sum(x => x.LineAmount);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string QuotationNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public DateOnly ExpiresOn { get; private set; }
    public QuotationStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>已由本报价转出的销售订单号；null 表示尚未转出。转出后再次转订单必须幂等返回该单号。</summary>
    public string? ConvertedSalesOrderNo { get; private set; }

    /// <summary>报价转出销售订单的 UTC 时间；与 <see cref="ConvertedSalesOrderNo"/> 同生共死。</summary>
    public DateTime? ConvertedAtUtc { get; private set; }

    public bool IsConverted => ConvertedSalesOrderNo is not null;
    public IReadOnlyCollection<QuotationLine> Lines => lines;

    public static Quotation Create(
        string organizationId,
        string environmentId,
        string quotationNo,
        string customerCode,
        DateOnly expiresOn,
        IEnumerable<QuotationLineDraft> lines)
    {
        return new Quotation(organizationId, environmentId, quotationNo, customerCode, expiresOn, lines);
    }

    public void Approve()
    {
        if (Status == QuotationStatus.Approved)
        {
            throw new InvalidOperationException("Approved quotations cannot be approved again.");
        }

        if (Status is QuotationStatus.Rejected or QuotationStatus.Expired)
        {
            throw new InvalidOperationException("Rejected or expired quotations cannot be approved.");
        }

        Status = QuotationStatus.Approved;
    }

    public void EnsureCanCreateSalesOrder(DateOnly today)
    {
        if (IsConverted)
        {
            throw new InvalidOperationException($"Quotation has already been converted to sales order '{ConvertedSalesOrderNo}'.");
        }

        if (Status != QuotationStatus.Approved)
        {
            throw new InvalidOperationException("Only approved quotations can create sales orders.");
        }

        if (ExpiresOn < today)
        {
            throw new InvalidOperationException("Expired quotations cannot create sales orders.");
        }
    }

    /// <summary>登记报价已转出的销售订单引用。仅允许在已批准且未转出时调用一次。</summary>
    public void MarkConvertedToSalesOrder(string salesOrderNo)
    {
        if (IsConverted)
        {
            throw new InvalidOperationException($"Quotation has already been converted to sales order '{ConvertedSalesOrderNo}'.");
        }

        if (Status != QuotationStatus.Approved)
        {
            throw new InvalidOperationException("Only approved quotations can be converted to sales orders.");
        }

        ConvertedSalesOrderNo = ErpText.Required(salesOrderNo, nameof(salesOrderNo));
        ConvertedAtUtc = DateTime.UtcNow;
    }
}

public sealed class QuotationLine : Entity<QuotationLineId>
{
    private QuotationLine()
    {
    }

    private QuotationLine(QuotationLineDraft draft)
    {
        LineNo = ErpText.Required(draft.LineNo, nameof(draft.LineNo));
        SkuCode = ErpText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = ErpText.Required(draft.UomCode, nameof(draft.UomCode));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        RequiredDate = draft.RequiredDate;
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateOnly RequiredDate { get; private set; }
    public decimal LineAmount => Quantity * UnitPrice;

    public static QuotationLine Create(QuotationLineDraft draft)
    {
        return new QuotationLine(draft);
    }
}
