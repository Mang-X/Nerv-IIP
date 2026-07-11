using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;

public partial record CreditNoteId : IGuidStronglyTypedId;

public sealed class CreditNote : Entity<CreditNoteId>, IAggregateRoot
{
    private CreditNote()
    {
    }

    private CreditNote(SalesReturnAuthorization rma, string creditNoteNo)
    {
        if (rma.Status != SalesReturnAuthorizationStatus.CreditIssued)
        {
            throw new InvalidOperationException("Credit notes require an RMA with an issued credit decision.");
        }

        OrganizationId = rma.OrganizationId;
        EnvironmentId = rma.EnvironmentId;
        CreditNoteNo = ErpText.Required(creditNoteNo, nameof(creditNoteNo));
        RmaNo = rma.RmaNo;
        AccountReceivableNo = rma.AccountReceivableNo;
        CustomerCode = rma.CustomerCode;
        CurrencyCode = rma.CurrencyCode;
        ExchangeRate = rma.ExchangeRate;
        Amount = rma.TotalAmount;
        LocalAmount = Amount * ExchangeRate;
        IssuedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CreditNoteNo { get; private set; } = string.Empty;
    public string RmaNo { get; private set; } = string.Empty;
    public string AccountReceivableNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal LocalAmount { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }

    public static CreditNote Issue(SalesReturnAuthorization rma, string creditNoteNo) => new(rma, creditNoteNo);
}
