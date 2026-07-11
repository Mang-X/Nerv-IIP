using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;

public partial record JournalVoucherId : IGuidStronglyTypedId;
public partial record JournalVoucherLineId : IGuidStronglyTypedId;

public sealed record JournalVoucherLineDraft(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    string Memo,
    string CurrencyCode = "CNY",
    decimal ExchangeRate = 1m,
    decimal? LocalDebitAmount = null,
    decimal? LocalCreditAmount = null);

public sealed class JournalVoucher : Entity<JournalVoucherId>, IAggregateRoot
{
    private readonly List<JournalVoucherLine> lines = [];

    private JournalVoucher()
    {
    }

    private JournalVoucher(string organizationId, string environmentId, string voucherNo, DateOnly postingDate, IEnumerable<JournalVoucherLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        VoucherNo = ErpText.Required(voucherNo, nameof(voucherNo));
        PostingDate = postingDate;
        lines.AddRange(lineDrafts.Select(x => JournalVoucherLine.Create(OrganizationId, EnvironmentId, x)));
        if (lines.Count < 2)
        {
            throw new ArgumentException("At least two voucher lines are required.", nameof(lineDrafts));
        }

        var debit = lines.Sum(x => x.LocalDebitAmount);
        var credit = lines.Sum(x => x.LocalCreditAmount);
        if (debit != credit)
        {
            throw new InvalidOperationException("Journal voucher local debits must equal local credits.");
        }

        PostedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new JournalVoucherPostedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string VoucherNo { get; private set; } = string.Empty;
    public DateOnly PostingDate { get; private set; }
    public DateTime PostedAtUtc { get; private set; }
    public IReadOnlyCollection<JournalVoucherLine> Lines => lines;

    public static JournalVoucher Post(string organizationId, string environmentId, string voucherNo, DateOnly postingDate, IEnumerable<JournalVoucherLineDraft> lines)
    {
        return new JournalVoucher(organizationId, environmentId, voucherNo, postingDate, lines);
    }

    public void Amend()
    {
        throw new InvalidOperationException("Posted journal vouchers are immutable.");
    }
}

public sealed class JournalVoucherLine : Entity<JournalVoucherLineId>
{
    private JournalVoucherLine()
    {
    }

    private JournalVoucherLine(string organizationId, string environmentId, JournalVoucherLineDraft draft)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        AccountCode = ErpText.Required(draft.AccountCode, nameof(draft.AccountCode));
        DebitAmount = draft.DebitAmount;
        CreditAmount = draft.CreditAmount;
        CurrencyCode = ErpText.Required(draft.CurrencyCode, nameof(draft.CurrencyCode)).ToUpperInvariant();
        ExchangeRate = ErpText.Positive(draft.ExchangeRate, nameof(draft.ExchangeRate));
        LocalDebitAmount = draft.LocalDebitAmount ?? DebitAmount * ExchangeRate;
        LocalCreditAmount = draft.LocalCreditAmount ?? CreditAmount * ExchangeRate;
        Memo = draft.Memo ?? string.Empty;
        if (DebitAmount < 0 || CreditAmount < 0 || (DebitAmount == 0 && CreditAmount == 0) || (DebitAmount > 0 && CreditAmount > 0))
        {
            throw new ArgumentException("Voucher lines must have exactly one non-zero debit or credit amount.", nameof(draft));
        }

        if (LocalDebitAmount < 0 || LocalCreditAmount < 0 || (LocalDebitAmount == 0 && LocalCreditAmount == 0) || (LocalDebitAmount > 0 && LocalCreditAmount > 0))
        {
            throw new ArgumentException("Voucher lines must have exactly one non-zero local debit or credit amount.", nameof(draft));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string AccountCode { get; private set; } = string.Empty;
    public decimal DebitAmount { get; private set; }
    public decimal CreditAmount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public decimal LocalDebitAmount { get; private set; }
    public decimal LocalCreditAmount { get; private set; }
    public string Memo { get; private set; } = string.Empty;

    public static JournalVoucherLine Create(string organizationId, string environmentId, JournalVoucherLineDraft draft)
    {
        return new JournalVoucherLine(organizationId, environmentId, draft);
    }
}
