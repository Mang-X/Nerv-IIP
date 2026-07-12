using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;

public partial record AccountPayableId : IGuidStronglyTypedId;

public sealed class AccountPayable : Entity<AccountPayableId>, IAggregateRoot
{
    private AccountPayable()
    {
    }

    private AccountPayable(
        string organizationId,
        string environmentId,
        string payableNo,
        string sourceDocumentNo,
        string supplierCode,
        decimal amount,
        string currencyCode,
        DateOnly? invoiceDate,
        DateOnly? dueDate,
        string? paymentTermCode,
        decimal exchangeRate)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        PayableNo = ErpText.Required(payableNo, nameof(payableNo));
        SourceDocumentNo = ErpText.Required(sourceDocumentNo, nameof(sourceDocumentNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ExchangeRate = ErpText.Positive(exchangeRate, nameof(exchangeRate));
        LocalAmount = Amount * ExchangeRate;
        CreatedAtUtc = DateTime.UtcNow;
        InvoiceDate = invoiceDate ?? DateOnly.FromDateTime(CreatedAtUtc);
        DueDate = dueDate ?? InvoiceDate.AddDays(30);
        PaymentTermCode = string.IsNullOrWhiteSpace(paymentTermCode) ? "NET30" : paymentTermCode.Trim().ToUpperInvariant();
        this.AddDomainEvent(new AccountPayableCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PayableNo { get; private set; } = string.Empty;
    public string SourceDocumentNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal DebitNoteAmount { get; private set; }
    public decimal LocalAmount { get; private set; }
    public decimal LocalPaidAmount { get; private set; }
    public decimal LocalDebitNoteAmount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string PaymentTermCode { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public decimal OpenAmount => Amount - PaidAmount - DebitNoteAmount;
    public decimal LocalOpenAmount => LocalAmount - LocalPaidAmount - LocalDebitNoteAmount;

    public static AccountPayable Create(
        string organizationId,
        string environmentId,
        string payableNo,
        string sourceDocumentNo,
        string supplierCode,
        decimal amount,
        string currencyCode,
        DateOnly? invoiceDate = null,
        DateOnly? dueDate = null,
        string? paymentTermCode = null,
        decimal exchangeRate = 1m)
    {
        return new AccountPayable(organizationId, environmentId, payableNo, sourceDocumentNo, supplierCode, amount, currencyCode, invoiceDate, dueDate, paymentTermCode, exchangeRate);
    }

    public void RegisterPayment(decimal amount)
    {
        _ = ErpText.Positive(amount, nameof(amount));
        if (amount > OpenAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Paid amount cannot exceed open payable amount.");
        }

        PaidAmount += amount;
        LocalPaidAmount += amount * ExchangeRate;
    }

    public void ApplyDebitNote(decimal amount)
    {
        _ = ErpText.Positive(amount, nameof(amount));
        if (amount > OpenAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Debit note amount cannot exceed open payable amount.");
        }

        DebitNoteAmount += amount;
        LocalDebitNoteAmount += amount * ExchangeRate;
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
