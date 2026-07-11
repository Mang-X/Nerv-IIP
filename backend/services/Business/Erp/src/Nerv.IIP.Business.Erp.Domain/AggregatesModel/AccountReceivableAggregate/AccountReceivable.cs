using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;

public partial record AccountReceivableId : IGuidStronglyTypedId;

public sealed class AccountReceivable : Entity<AccountReceivableId>, IAggregateRoot
{
    private AccountReceivable()
    {
    }

    private AccountReceivable(
        string organizationId,
        string environmentId,
        string receivableNo,
        string sourceDocumentNo,
        string customerCode,
        decimal amount,
        string currencyCode,
        DateOnly? invoiceDate,
        DateOnly? dueDate,
        string? paymentTermCode,
        decimal exchangeRate)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        ReceivableNo = ErpText.Required(receivableNo, nameof(receivableNo));
        SourceDocumentNo = ErpText.Required(sourceDocumentNo, nameof(sourceDocumentNo));
        CustomerCode = ErpText.Required(customerCode, nameof(customerCode));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ExchangeRate = ErpText.Positive(exchangeRate, nameof(exchangeRate));
        LocalAmount = Amount * ExchangeRate;
        CreatedAtUtc = DateTime.UtcNow;
        InvoiceDate = invoiceDate ?? DateOnly.FromDateTime(CreatedAtUtc);
        DueDate = dueDate ?? InvoiceDate.AddDays(30);
        PaymentTermCode = string.IsNullOrWhiteSpace(paymentTermCode) ? "NET30" : paymentTermCode.Trim().ToUpperInvariant();
        this.AddDomainEvent(new AccountReceivableCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReceivableNo { get; private set; } = string.Empty;
    public string SourceDocumentNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public decimal CollectedAmount { get; private set; }
    public decimal CreditNoteAmount { get; private set; }
    public decimal LocalAmount { get; private set; }
    public decimal LocalCollectedAmount { get; private set; }
    public decimal LocalCreditNoteAmount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string PaymentTermCode { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public decimal OpenAmount => Amount - CollectedAmount - CreditNoteAmount;
    public decimal LocalOpenAmount => LocalAmount - LocalCollectedAmount - LocalCreditNoteAmount;

    public static AccountReceivable Create(
        string organizationId,
        string environmentId,
        string receivableNo,
        string sourceDocumentNo,
        string customerCode,
        decimal amount,
        string currencyCode,
        DateOnly? invoiceDate = null,
        DateOnly? dueDate = null,
        string? paymentTermCode = null,
        decimal exchangeRate = 1m)
    {
        return new AccountReceivable(organizationId, environmentId, receivableNo, sourceDocumentNo, customerCode, amount, currencyCode, invoiceDate, dueDate, paymentTermCode, exchangeRate);
    }

    public void RegisterCollection(decimal amount)
    {
        _ = ErpText.Positive(amount, nameof(amount));
        if (amount > OpenAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Collected amount cannot exceed open receivable amount.");
        }

        CollectedAmount += amount;
        LocalCollectedAmount += amount * ExchangeRate;
    }

    public void ApplyCreditNote(decimal amount)
    {
        _ = ErpText.Positive(amount, nameof(amount));
        if (amount > OpenAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Credit note amount cannot exceed open receivable amount.");
        }

        CreditNoteAmount += amount;
        LocalCreditNoteAmount += amount * ExchangeRate;
    }

    public string GetAgingBucket(DateOnly asOfDate)
    {
        if (OpenAmount <= 0)
        {
            return "settled";
        }

        var daysPastDue = asOfDate.DayNumber - DueDate.DayNumber;
        return daysPastDue <= 0
            ? "current"
            : daysPastDue <= 30
                ? "1-30"
                : daysPastDue <= 60
                    ? "31-60"
                    : daysPastDue <= 90
                        ? "61-90"
                        : "90+";
    }
}
