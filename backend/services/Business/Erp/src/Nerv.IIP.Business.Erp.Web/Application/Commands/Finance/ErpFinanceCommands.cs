using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

public sealed record CreateAccountPayableCommand(
    string OrganizationId,
    string EnvironmentId,
    string? PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    string CurrencyCode,
    DateOnly? InvoiceDate = null,
    DateOnly? DueDate = null,
    string? PaymentTermCode = null,
    string? IdempotencyKey = null) : ICommand<AccountPayableId>;

public sealed class CreateAccountPayableCommandValidator : AbstractValidator<CreateAccountPayableCommand>
{
    public CreateAccountPayableCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PayableNo).MaximumLength(100);
        RuleFor(x => x.SourceDocumentNo).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
    }
}

public sealed class CreateAccountPayableCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateAccountPayableCommand, AccountPayableId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<AccountPayableId> Handle(CreateAccountPayableCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-payable", request.PayableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountPayables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.PayableNo == allocation.Code, cancellationToken)).Id;
        }

        var payable = AccountPayable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode);
        dbContext.AccountPayables.Add(payable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForAccountPayable(payable));
        return payable.Id;
    }
}

public sealed record CreateAccountReceivableCommand(
    string OrganizationId,
    string EnvironmentId,
    string? ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    string CurrencyCode,
    DateOnly? InvoiceDate = null,
    DateOnly? DueDate = null,
    string? PaymentTermCode = null,
    string? IdempotencyKey = null) : ICommand<AccountReceivableId>;

public sealed class CreateAccountReceivableCommandValidator : AbstractValidator<CreateAccountReceivableCommand>
{
    public CreateAccountReceivableCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ReceivableNo).MaximumLength(100);
        RuleFor(x => x.SourceDocumentNo).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CustomerCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
    }
}

public sealed class CreateAccountReceivableCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateAccountReceivableCommand, AccountReceivableId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<AccountReceivableId> Handle(CreateAccountReceivableCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-receivable", request.ReceivableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountReceivables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.ReceivableNo == allocation.Code, cancellationToken)).Id;
        }

        var receivable = AccountReceivable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode);
        dbContext.AccountReceivables.Add(receivable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForAccountReceivable(receivable));
        return receivable.Id;
    }
}

public sealed record CreateCostCandidateCommand(string OrganizationId, string EnvironmentId, string? CandidateNo, string SourceType, string SourceDocumentNo, decimal Amount, string CurrencyCode, string? IdempotencyKey = null) : ICommand<CostCandidateId>;

public sealed class CreateCostCandidateCommandValidator : AbstractValidator<CreateCostCandidateCommand>
{
    public CreateCostCandidateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CandidateNo).MaximumLength(100);
        RuleFor(x => x.SourceType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentNo).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
    }
}

public sealed class CreateCostCandidateCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateCostCandidateCommand, CostCandidateId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<CostCandidateId> Handle(CreateCostCandidateCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "cost-candidate", request.CandidateNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceType, request.SourceDocumentNo, request.Amount, request.CurrencyCode), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.CostCandidates.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.CandidateNo == allocation.Code, cancellationToken)).Id;
        }

        var candidate = CostCandidate.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceType, request.SourceDocumentNo, request.Amount, request.CurrencyCode);
        dbContext.CostCandidates.Add(candidate);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForCostCandidate(candidate));
        return candidate.Id;
    }
}

public sealed record RegisterAccountPayablePaymentCommand(
    string OrganizationId,
    string EnvironmentId,
    string PayableNo,
    decimal Amount,
    DateOnly PaymentDate,
    string CashAccountCode,
    string IdempotencyKey) : ICommand;

public sealed class RegisterAccountPayablePaymentCommandValidator : AbstractValidator<RegisterAccountPayablePaymentCommand>
{
    public RegisterAccountPayablePaymentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PayableNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.PaymentDate).NotEqual(default(DateOnly));
        RuleFor(x => x.CashAccountCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class RegisterAccountPayablePaymentCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RegisterAccountPayablePaymentCommand>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task Handle(RegisterAccountPayablePaymentCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable-payment",
            null,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PayableNo, request.Amount, request.PaymentDate, request.CashAccountCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay
            && await dbContext.JournalVouchers.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.VoucherNo == allocation.Code,
                cancellationToken))
        {
            return;
        }

        var payable = await dbContext.AccountPayables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PayableNo == request.PayableNo,
            cancellationToken)
            ?? throw new KnownException($"Account payable '{request.PayableNo}' was not found.");

        payable.RegisterPayment(request.Amount);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForPayablePayment(payable, allocation.Code, request.Amount, request.PaymentDate, request.CashAccountCode));
    }
}

public sealed record RegisterAccountReceivableCollectionCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReceivableNo,
    decimal Amount,
    DateOnly CollectionDate,
    string CashAccountCode,
    string IdempotencyKey) : ICommand;

public sealed class RegisterAccountReceivableCollectionCommandValidator : AbstractValidator<RegisterAccountReceivableCollectionCommand>
{
    public RegisterAccountReceivableCollectionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ReceivableNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CollectionDate).NotEqual(default(DateOnly));
        RuleFor(x => x.CashAccountCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class RegisterAccountReceivableCollectionCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RegisterAccountReceivableCollectionCommand>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task Handle(RegisterAccountReceivableCollectionCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-receivable-collection",
            null,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.ReceivableNo, request.Amount, request.CollectionDate, request.CashAccountCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay
            && await dbContext.JournalVouchers.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.VoucherNo == allocation.Code,
                cancellationToken))
        {
            return;
        }

        var receivable = await dbContext.AccountReceivables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ReceivableNo == request.ReceivableNo,
            cancellationToken)
            ?? throw new KnownException($"Account receivable '{request.ReceivableNo}' was not found.");

        receivable.RegisterCollection(request.Amount);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForReceivableCollection(receivable, allocation.Code, request.Amount, request.CollectionDate, request.CashAccountCode));
    }
}

public sealed record JournalVoucherCommandLine(string AccountCode, decimal DebitAmount, decimal CreditAmount, string Memo);

public sealed record PostJournalVoucherCommand(string OrganizationId, string EnvironmentId, string? VoucherNo, DateOnly PostingDate, IReadOnlyCollection<JournalVoucherCommandLine> Lines, string? IdempotencyKey = null) : ICommand<JournalVoucherId>;

public sealed class PostJournalVoucherCommandValidator : AbstractValidator<PostJournalVoucherCommand>
{
    public PostJournalVoucherCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.VoucherNo).MaximumLength(100);
        RuleFor(x => x.PostingDate).NotEqual(default(DateOnly));
        RuleFor(x => x.Lines).NotEmpty().Must(x => x.Count >= 2).WithMessage("At least two voucher lines are required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.AccountCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.DebitAmount).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.CreditAmount).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.Memo).MaximumLength(250);
            line.RuleFor(x => x)
                .Must(x => (x.DebitAmount > 0 && x.CreditAmount == 0) || (x.CreditAmount > 0 && x.DebitAmount == 0))
                .WithMessage("Voucher lines must have exactly one non-zero debit or credit amount.");
        });
        RuleFor(x => x.Lines)
            .Must(x => x.Sum(line => line.DebitAmount) == x.Sum(line => line.CreditAmount))
            .WithMessage("Journal voucher debits must equal credits.");
    }
}

public sealed class PostJournalVoucherCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<PostJournalVoucherCommand, JournalVoucherId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<JournalVoucherId> Handle(PostJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "journal-voucher", request.VoucherNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.PostingDate, request.Lines.Select(x => $"{x.AccountCode}:{x.DebitAmount}:{x.CreditAmount}:{x.Memo}")), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.JournalVouchers.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.VoucherNo == allocation.Code, cancellationToken)).Id;
        }

        var voucher = JournalVoucher.Post(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.PostingDate,
            request.Lines.Select(x => new JournalVoucherLineDraft(x.AccountCode, x.DebitAmount, x.CreditAmount, x.Memo)));
        dbContext.JournalVouchers.Add(voucher);
        return voucher.Id;
    }
}

internal static class FinanceVoucherFactory
{
    public const string InventoryAccountCode = "1401";
    public const string AccountsPayableAccountCode = "2202";
    public const string DirectPayableExpenseAccountCode = "5001";
    public const string GoodsReceiptInvoiceReceiptAccountCode = "GR-IR";

    public static string GoodsReceiptIrAccrualVoucherNo(string purchaseReceiptNo)
    {
        return $"JV-GRIR-{purchaseReceiptNo}";
    }

    public static JournalVoucher ForGoodsReceiptIrAccrual(PurchaseReceipt receipt, decimal amount, string voucherNo)
    {
        return JournalVoucher.Post(
            receipt.OrganizationId,
            receipt.EnvironmentId,
            voucherNo,
            DateOnly.FromDateTime(receipt.RecordedAtUtc),
            [
                new JournalVoucherLineDraft(InventoryAccountCode, amount, 0m, $"Goods receipt {receipt.PurchaseReceiptNo}"),
                new JournalVoucherLineDraft(GoodsReceiptInvoiceReceiptAccountCode, 0m, amount, $"GR/IR accrual {receipt.PurchaseReceiptNo}"),
            ]);
    }

    public static JournalVoucher ForSupplierInvoiceGrIrClearing(SupplierInvoice invoice, AccountPayable payable)
    {
        return JournalVoucher.Post(
            invoice.OrganizationId,
            invoice.EnvironmentId,
            $"JV-AP-{payable.PayableNo}",
            invoice.InvoiceDate,
            [
                new JournalVoucherLineDraft(GoodsReceiptInvoiceReceiptAccountCode, invoice.TotalAmount, 0m, $"Clear GR/IR for receipt {invoice.PurchaseReceiptNo}"),
                new JournalVoucherLineDraft(AccountsPayableAccountCode, 0m, invoice.TotalAmount, $"AP {payable.PayableNo}"),
            ]);
    }

    public static JournalVoucher ForAccountPayable(AccountPayable payable)
    {
        return JournalVoucher.Post(
            payable.OrganizationId,
            payable.EnvironmentId,
            $"JV-AP-{payable.PayableNo}",
            payable.InvoiceDate,
            [
                new JournalVoucherLineDraft(DirectPayableExpenseAccountCode, payable.Amount, 0m, $"Direct AP expense {payable.SourceDocumentNo}"),
                new JournalVoucherLineDraft(AccountsPayableAccountCode, 0m, payable.Amount, $"AP {payable.PayableNo}"),
            ]);
    }

    public static JournalVoucher ForAccountReceivable(AccountReceivable receivable)
    {
        return JournalVoucher.Post(
            receivable.OrganizationId,
            receivable.EnvironmentId,
            $"JV-AR-{receivable.ReceivableNo}",
            receivable.InvoiceDate,
            [
                new JournalVoucherLineDraft("1122", receivable.Amount, 0m, $"AR {receivable.ReceivableNo}"),
                new JournalVoucherLineDraft("6001", 0m, receivable.Amount, $"AR source {receivable.SourceDocumentNo}"),
            ]);
    }

    public static JournalVoucher ForCostCandidate(CostCandidate candidate)
    {
        return JournalVoucher.Post(
            candidate.OrganizationId,
            candidate.EnvironmentId,
            $"JV-COST-{candidate.CandidateNo}",
            DateOnly.FromDateTime(candidate.CreatedAtUtc),
            [
                new JournalVoucherLineDraft("5001", candidate.Amount, 0m, $"Cost candidate {candidate.SourceDocumentNo}"),
                new JournalVoucherLineDraft("1401", 0m, candidate.Amount, $"Cost source {candidate.SourceType}"),
            ]);
    }

    public static JournalVoucher ForPayablePayment(AccountPayable payable, string voucherNo, decimal amount, DateOnly paymentDate, string cashAccountCode)
    {
        return JournalVoucher.Post(
            payable.OrganizationId,
            payable.EnvironmentId,
            voucherNo,
            paymentDate,
            [
                new JournalVoucherLineDraft(AccountsPayableAccountCode, amount, 0m, $"Pay AP {payable.PayableNo}"),
                new JournalVoucherLineDraft(cashAccountCode, 0m, amount, $"Cash payment for {payable.PayableNo}"),
            ]);
    }

    public static JournalVoucher ForReceivableCollection(AccountReceivable receivable, string voucherNo, decimal amount, DateOnly collectionDate, string cashAccountCode)
    {
        return JournalVoucher.Post(
            receivable.OrganizationId,
            receivable.EnvironmentId,
            voucherNo,
            collectionDate,
            [
                new JournalVoucherLineDraft(cashAccountCode, amount, 0m, $"Cash collection for {receivable.ReceivableNo}"),
                new JournalVoucherLineDraft("1122", 0m, amount, $"Collect AR {receivable.ReceivableNo}"),
            ]);
    }
}
