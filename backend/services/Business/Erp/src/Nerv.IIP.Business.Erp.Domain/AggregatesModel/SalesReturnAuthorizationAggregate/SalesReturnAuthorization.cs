using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;

public partial record SalesReturnAuthorizationId : IGuidStronglyTypedId;
public partial record SalesReturnAuthorizationLineId : IGuidStronglyTypedId;

public enum SalesReturnAuthorizationStatus
{
    Authorized = 0,
    WarehouseReceived = 1,
    CreditApproved = 2,
    CreditIssued = 3,
    CreditDenied = 4,
}

public sealed record SalesReturnAuthorizationLineDraft(
    string SalesOrderLineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    string LocationCode,
    string? LotNo);

public sealed class SalesReturnAuthorization : Entity<SalesReturnAuthorizationId>, IAggregateRoot
{
    private readonly List<SalesReturnAuthorizationLine> lines = [];

    private SalesReturnAuthorization()
    {
    }

    private SalesReturnAuthorization(
        string organizationId,
        string environmentId,
        string rmaNo,
        string salesOrderNo,
        string accountReceivableNo,
        string customerCode,
        string siteCode,
        string currencyCode,
        decimal exchangeRate,
        IEnumerable<SalesReturnAuthorizationLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        RmaNo = ErpText.Required(rmaNo, nameof(rmaNo));
        SalesOrderNo = ErpText.Required(salesOrderNo, nameof(salesOrderNo));
        AccountReceivableNo = ErpText.Required(accountReceivableNo, nameof(accountReceivableNo));
        CustomerCode = ErpText.Required(customerCode, nameof(customerCode));
        SiteCode = ErpText.Required(siteCode, nameof(siteCode));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ExchangeRate = ErpText.Positive(exchangeRate, nameof(exchangeRate));
        lines.AddRange(lineDrafts.Select(SalesReturnAuthorizationLine.Create));
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one RMA line is required.", nameof(lineDrafts));
        }

        Status = SalesReturnAuthorizationStatus.Authorized;
        AuthorizedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new SalesReturnAuthorizedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RmaNo { get; private set; } = string.Empty;
    public string SalesOrderNo { get; private set; } = string.Empty;
    public string AccountReceivableNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public SalesReturnAuthorizationStatus Status { get; private set; }
    public string? WmsInboundOrderNo { get; private set; }
    public string? QualityDisposition { get; private set; }
    public string? CreditNoteNo { get; private set; }
    public DateTime AuthorizedAtUtc { get; private set; }
    public DateTime? WarehouseReceivedAtUtc { get; private set; }
    public DateTime? QualityDispositionAtUtc { get; private set; }
    public DateTime? CreditIssuedAtUtc { get; private set; }
    public IReadOnlyCollection<SalesReturnAuthorizationLine> Lines => lines;
    public decimal TotalAmount => lines.Sum(x => x.LineAmount);

    public static SalesReturnAuthorization Authorize(
        string organizationId,
        string environmentId,
        string rmaNo,
        string salesOrderNo,
        string accountReceivableNo,
        string customerCode,
        string siteCode,
        string currencyCode,
        decimal exchangeRate,
        IEnumerable<SalesReturnAuthorizationLineDraft> lines)
    {
        return new SalesReturnAuthorization(
            organizationId,
            environmentId,
            rmaNo,
            salesOrderNo,
            accountReceivableNo,
            customerCode,
            siteCode,
            currencyCode,
            exchangeRate,
            lines);
    }

    public void MarkWarehouseReceived(string wmsInboundOrderNo)
    {
        var normalizedInboundNo = ErpText.Required(wmsInboundOrderNo, nameof(wmsInboundOrderNo));
        if (Status == SalesReturnAuthorizationStatus.WarehouseReceived && WmsInboundOrderNo == normalizedInboundNo)
        {
            return;
        }

        if (Status != SalesReturnAuthorizationStatus.Authorized)
        {
            throw new InvalidOperationException("Only authorized RMAs can record warehouse receipt.");
        }

        WmsInboundOrderNo = normalizedInboundNo;
        WarehouseReceivedAtUtc = DateTime.UtcNow;
        Status = SalesReturnAuthorizationStatus.WarehouseReceived;
    }

    public void ApplyQualityDisposition(string disposition)
    {
        if (Status is SalesReturnAuthorizationStatus.CreditIssued or SalesReturnAuthorizationStatus.CreditDenied)
        {
            throw new InvalidOperationException("A completed RMA credit decision is immutable.");
        }

        if (Status != SalesReturnAuthorizationStatus.WarehouseReceived)
        {
            throw new InvalidOperationException("Warehouse receipt is required before applying RMA quality disposition.");
        }

        var normalizedDisposition = ErpText.Required(disposition, nameof(disposition)).Trim().ToLowerInvariant();
        Status = normalizedDisposition switch
        {
            "passed" or "conditional-release" => SalesReturnAuthorizationStatus.CreditApproved,
            "rejected" => SalesReturnAuthorizationStatus.CreditDenied,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "RMA quality disposition must be passed, conditional-release, or rejected."),
        };
        QualityDisposition = normalizedDisposition;
        QualityDispositionAtUtc = DateTime.UtcNow;
    }

    public void MarkCreditIssued(string creditNoteNo)
    {
        if (Status == SalesReturnAuthorizationStatus.CreditIssued
            && string.Equals(CreditNoteNo, creditNoteNo, StringComparison.Ordinal))
        {
            return;
        }

        if (Status != SalesReturnAuthorizationStatus.CreditApproved)
        {
            throw new InvalidOperationException("Only credit-approved RMAs can issue a credit note.");
        }

        CreditNoteNo = ErpText.Required(creditNoteNo, nameof(creditNoteNo));
        CreditIssuedAtUtc = DateTime.UtcNow;
        Status = SalesReturnAuthorizationStatus.CreditIssued;
    }
}

public sealed class SalesReturnAuthorizationLine : Entity<SalesReturnAuthorizationLineId>
{
    private SalesReturnAuthorizationLine()
    {
    }

    private SalesReturnAuthorizationLine(SalesReturnAuthorizationLineDraft draft)
    {
        SalesOrderLineNo = ErpText.Required(draft.SalesOrderLineNo, nameof(draft.SalesOrderLineNo));
        SkuCode = ErpText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = ErpText.Required(draft.UomCode, nameof(draft.UomCode));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        LocationCode = ErpText.Required(draft.LocationCode, nameof(draft.LocationCode));
        LotNo = string.IsNullOrWhiteSpace(draft.LotNo) ? null : draft.LotNo.Trim();
    }

    public string SalesOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string LocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public decimal LineAmount => Quantity * UnitPrice;

    public static SalesReturnAuthorizationLine Create(SalesReturnAuthorizationLineDraft draft) => new(draft);
}
