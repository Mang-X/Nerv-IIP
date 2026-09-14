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

    private JournalVoucher(
        string organizationId,
        string environmentId,
        string voucherNo,
        DateOnly postingDate,
        IEnumerable<JournalVoucherLineDraft> lineDrafts,
        JournalVoucherSourceType sourceType,
        string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        VoucherNo = ErpText.Required(voucherNo, nameof(voucherNo));
        SourceType = sourceType.Code;
        SourceNo = ErpText.Required(sourceNo, nameof(sourceNo));
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

    /// <summary>
    /// 来源单据类型码（<see cref="JournalVoucherSourceType.Code"/>）。#3278 / S2 新增。
    ///
    /// **可空**：列在数据库上是可空的，本次不回填存量行（owner 2026-09-14 裁定：演示库数据可重造），
    /// 所以从旧行读回来就是 <see langword="null"/>。**新写入的行一律非空**——
    /// <see cref="Post"/> 是唯一构造入口且这两个参数不可省略，
    /// 空串/空白由 <c>ErpText.Required</c> 在构造期拒绝。
    /// </summary>
    public string? SourceType { get; private set; }

    /// <summary>
    /// 来源单据号。语义由 <see cref="SourceType"/> 决定（应付单号 / 发票号 / 收款单号 / 移动号 …）。
    /// 可空性与写入保证同 <see cref="SourceType"/>。
    /// </summary>
    public string? SourceNo { get; private set; }

    public DateOnly PostingDate { get; private set; }
    public DateTime PostedAtUtc { get; private set; }
    public IReadOnlyCollection<JournalVoucherLine> Lines => lines;

    /// <summary>
    /// 记账凭证的**唯一**构造入口（构造函数私有）。
    ///
    /// ⭐ <paramref name="sourceType"/> 与 <paramref name="sourceNo"/> **没有默认值，故不可省略**：
    /// 这是 #3278 / S2 「17 个建凭证位点一个都不能漏」的承重装置——漏填在编译期就是 CS7036，
    /// 不会退化成「某一行来源列恒空而门禁照绿」。**失效方向**有两条，都不由这个签名兜住：
    /// ① 有人加一个带默认值的 <c>Post</c> 重载（由 <c>JournalVoucherSourceContractTests</c> 的反射断言看住）；
    /// ② 有人绕开 EF 用原生 SQL 直接 INSERT（S2 落地时实测 Erp 生产代码 <c>ExecuteSql</c>/<c>FromSql</c> 零命中，
    ///    日后新增会让这个保证静默失效）。
    /// </summary>
    public static JournalVoucher Post(
        string organizationId,
        string environmentId,
        string voucherNo,
        DateOnly postingDate,
        IEnumerable<JournalVoucherLineDraft> lines,
        JournalVoucherSourceType sourceType,
        string sourceNo)
    {
        return new JournalVoucher(organizationId, environmentId, voucherNo, postingDate, lines, sourceType, sourceNo);
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
