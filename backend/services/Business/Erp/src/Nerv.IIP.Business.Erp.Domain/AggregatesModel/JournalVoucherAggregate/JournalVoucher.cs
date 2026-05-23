using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;

public partial record JournalVoucherId : IGuidStronglyTypedId;
public partial record JournalVoucherLineId : IGuidStronglyTypedId;

public sealed record JournalVoucherLineDraft(string AccountCode, decimal DebitAmount, decimal CreditAmount, string Memo);

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
        lines.AddRange(lineDrafts.Select(JournalVoucherLine.Create));
        if (lines.Count < 2)
        {
            throw new ArgumentException("At least two voucher lines are required.", nameof(lineDrafts));
        }

        var debit = lines.Sum(x => x.DebitAmount);
        var credit = lines.Sum(x => x.CreditAmount);
        if (debit != credit)
        {
            throw new InvalidOperationException("Journal voucher debits must equal credits.");
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

    private JournalVoucherLine(JournalVoucherLineDraft draft)
    {
        AccountCode = ErpText.Required(draft.AccountCode, nameof(draft.AccountCode));
        DebitAmount = draft.DebitAmount;
        CreditAmount = draft.CreditAmount;
        Memo = draft.Memo ?? string.Empty;
        if (DebitAmount < 0 || CreditAmount < 0 || (DebitAmount == 0 && CreditAmount == 0) || (DebitAmount > 0 && CreditAmount > 0))
        {
            throw new ArgumentException("Voucher lines must have exactly one non-zero debit or credit amount.", nameof(draft));
        }
    }

    public string AccountCode { get; private set; } = string.Empty;
    public decimal DebitAmount { get; private set; }
    public decimal CreditAmount { get; private set; }
    public string Memo { get; private set; } = string.Empty;

    public static JournalVoucherLine Create(JournalVoucherLineDraft draft)
    {
        return new JournalVoucherLine(draft);
    }
}
