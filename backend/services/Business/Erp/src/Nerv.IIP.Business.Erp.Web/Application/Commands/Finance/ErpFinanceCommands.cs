using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

public sealed record OpenAccountingPeriodCommand(
    string OrganizationId,
    string EnvironmentId,
    string PeriodCode,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand<AccountingPeriodId>;

public sealed class OpenAccountingPeriodCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<OpenAccountingPeriodCommand, AccountingPeriodId>
{
    public async Task<AccountingPeriodId> Handle(OpenAccountingPeriodCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.AccountingPeriods.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PeriodCode == request.PeriodCode,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var period = AccountingPeriod.Open(request.OrganizationId, request.EnvironmentId, request.PeriodCode, request.StartDate, request.EndDate);
        dbContext.AccountingPeriods.Add(period);
        return period.Id;
    }
}

public sealed record CloseAccountingPeriodCommand(
    string OrganizationId,
    string EnvironmentId,
    string PeriodCode,
    string ClosedBy,
    string Reason) : ICommand;

public sealed class CloseAccountingPeriodCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CloseAccountingPeriodCommand>
{
    public async Task Handle(CloseAccountingPeriodCommand request, CancellationToken cancellationToken)
    {
        var period = await AccountingPeriodPostingGuard.FindPeriodAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.PeriodCode, cancellationToken);
        period.Close(request.ClosedBy, request.Reason);
    }
}

public sealed record ReopenAccountingPeriodCommand(
    string OrganizationId,
    string EnvironmentId,
    string PeriodCode,
    string ReopenedBy,
    string Reason) : ICommand;

public sealed class ReopenAccountingPeriodCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReopenAccountingPeriodCommand>
{
    public async Task Handle(ReopenAccountingPeriodCommand request, CancellationToken cancellationToken)
    {
        var period = await AccountingPeriodPostingGuard.FindPeriodAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.PeriodCode, cancellationToken);
        period.Reopen(request.ReopenedBy, request.Reason);
    }
}

internal static class AccountingPeriodPostingGuard
{
    public static async Task<AccountingPeriod> FindPeriodAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string periodCode,
        CancellationToken cancellationToken)
    {
        return await dbContext.AccountingPeriods.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.PeriodCode == periodCode,
            cancellationToken)
            ?? throw new KnownException($"Accounting period '{periodCode}' was not found.");
    }

    public static async Task EnsureOpenAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        DateOnly postingDate,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        var period = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.StartDate <= postingDate
                && x.EndDate >= postingDate)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (period is null || period.CanPost)
        {
            return;
        }

        throw new KnownException($"Cannot post {sourceDescription} into closed accounting period '{period.PeriodCode}'. Reopen the period with an auditable reason before posting late integration or manual voucher facts.");
    }
}

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
    string? IdempotencyKey = null,
    decimal ExchangeRate = 1m) : ICommand<AccountPayableId>;

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
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public sealed class CreateAccountPayableCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateAccountPayableCommand, AccountPayableId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<AccountPayableId> Handle(CreateAccountPayableCommand request, CancellationToken cancellationToken)
    {
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            "account payable voucher",
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-payable", request.PayableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountPayables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.PayableNo == allocation.Code, cancellationToken)).Id;
        }

        var payable = AccountPayable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate);
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
    string? IdempotencyKey = null,
    decimal ExchangeRate = 1m) : ICommand<AccountReceivableId>;

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
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public sealed class CreateAccountReceivableCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateAccountReceivableCommand, AccountReceivableId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<AccountReceivableId> Handle(CreateAccountReceivableCommand request, CancellationToken cancellationToken)
    {
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            "account receivable voucher",
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-receivable", request.ReceivableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountReceivables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.ReceivableNo == allocation.Code, cancellationToken)).Id;
        }

        var receivable = AccountReceivable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate);
        dbContext.AccountReceivables.Add(receivable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForAccountReceivable(receivable));
        return receivable.Id;
    }
}

public sealed record CreateCostCandidateCommand(string OrganizationId, string EnvironmentId, string? CandidateNo, string SourceType, string SourceDocumentNo, decimal Amount, string CurrencyCode, string? IdempotencyKey = null, decimal ExchangeRate = 1m) : ICommand<CostCandidateId>;

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
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public sealed class CreateCostCandidateCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateCostCandidateCommand, CostCandidateId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<CostCandidateId> Handle(CreateCostCandidateCommand request, CancellationToken cancellationToken)
    {
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "cost candidate voucher",
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "cost-candidate", request.CandidateNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceType, request.SourceDocumentNo, request.Amount, request.CurrencyCode, request.ExchangeRate), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.CostCandidates.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.CandidateNo == allocation.Code, cancellationToken)).Id;
        }

        var candidate = CostCandidate.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceType, request.SourceDocumentNo, request.Amount, request.CurrencyCode, request.ExchangeRate);
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
    string IdempotencyKey,
    string? PaymentCurrencyCode = null,
    decimal PaymentExchangeRate = 1m,
    IReadOnlyCollection<PayablePaymentAllocationCommandLine>? Allocations = null) : ICommand;

public sealed record PayablePaymentAllocationCommandLine(string PayableNo, decimal Amount);

public sealed class RegisterAccountPayablePaymentCommandValidator : AbstractValidator<RegisterAccountPayablePaymentCommand>
{
    public RegisterAccountPayablePaymentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PayableNo)
            .MaximumLength(100)
            .Must((command, payableNo) => !string.IsNullOrWhiteSpace(payableNo) || command.Allocations is { Count: > 0 })
            .WithMessage("PayableNo is required when allocations are not supplied.");
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.PaymentDate).NotEqual(default(DateOnly));
        RuleFor(x => x.CashAccountCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PaymentCurrencyCode).MaximumLength(10);
        RuleFor(x => x.PaymentExchangeRate).GreaterThan(0);
        RuleForEach(x => x.Allocations).ChildRules(line =>
        {
            line.RuleFor(x => x.PayableNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Amount).GreaterThan(0);
        });
    }
}

public sealed class RegisterAccountPayablePaymentCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RegisterAccountPayablePaymentCommand>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task Handle(RegisterAccountPayablePaymentCommand request, CancellationToken cancellationToken)
    {
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.PaymentDate,
            "payment execution voucher",
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable-payment",
            null,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PayableNo, request.Amount, request.PaymentDate, request.CashAccountCode, request.PaymentCurrencyCode, request.PaymentExchangeRate, request.Allocations?.Select(x => $"{x.PayableNo}:{x.Amount}")),
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

        var allocationLines = PaymentExecutionCommandFacts.NormalizeAllocationLines(request.PayableNo, request.Amount, request.Allocations);
        var voucherAllocations = await PaymentExecutionCommandFacts.LoadPayableVoucherAllocationsAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            allocationLines,
            request.Amount,
            cancellationToken);
        var supplierCode = PaymentExecutionCommandFacts.ResolveSingleSupplierCode(voucherAllocations);
        foreach (var voucherAllocation in voucherAllocations)
        {
            voucherAllocation.Payable.RegisterPayment(voucherAllocation.Amount);
        }

        var paymentCurrencyCode = string.IsNullOrWhiteSpace(request.PaymentCurrencyCode)
            ? voucherAllocations[0].Payable.CurrencyCode
            : request.PaymentCurrencyCode.Trim().ToUpperInvariant();
        var paymentExecution = PaymentExecution.Approve(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            supplierCode,
            request.Amount,
            paymentCurrencyCode,
            request.PaymentExchangeRate,
            request.PaymentDate,
            request.CashAccountCode,
            "system:business-erp",
            allocationLines.Select(x => new PaymentExecutionAllocationDraft(x.PayableNo, x.Amount)).ToArray());
        paymentExecution.Execute("system:business-erp");
        dbContext.PaymentExecutions.Add(paymentExecution);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForPayablePayment(
            voucherAllocations,
            allocation.Code,
            request.Amount,
            paymentCurrencyCode,
            request.PaymentExchangeRate,
            request.PaymentDate,
            request.CashAccountCode));
    }
}

public sealed record ApprovePaymentExecutionCommand(
    string OrganizationId,
    string EnvironmentId,
    string PayableNo,
    decimal Amount,
    DateOnly PaymentDate,
    string CashAccountCode,
    string IdempotencyKey,
    string? PaymentCurrencyCode = null,
    decimal PaymentExchangeRate = 1m,
    IReadOnlyCollection<PayablePaymentAllocationCommandLine>? Allocations = null) : ICommand<string>;

public sealed class ApprovePaymentExecutionCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<ApprovePaymentExecutionCommand, string>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<string> Handle(ApprovePaymentExecutionCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable-payment",
            null,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PayableNo, request.Amount, request.PaymentDate, request.CashAccountCode, request.PaymentCurrencyCode, request.PaymentExchangeRate, request.Allocations?.Select(x => $"{x.PayableNo}:{x.Amount}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay
            && await dbContext.PaymentExecutions.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.PaymentExecutionNo == allocation.Code,
                cancellationToken))
        {
            return allocation.Code;
        }

        var allocationLines = PaymentExecutionCommandFacts.NormalizeAllocationLines(request.PayableNo, request.Amount, request.Allocations);
        var voucherAllocations = await PaymentExecutionCommandFacts.LoadPayableVoucherAllocationsAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            allocationLines,
            request.Amount,
            cancellationToken);
        var paymentCurrencyCode = string.IsNullOrWhiteSpace(request.PaymentCurrencyCode)
            ? voucherAllocations[0].Payable.CurrencyCode
            : request.PaymentCurrencyCode.Trim().ToUpperInvariant();

        dbContext.PaymentExecutions.Add(PaymentExecution.Approve(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            PaymentExecutionCommandFacts.ResolveSingleSupplierCode(voucherAllocations),
            request.Amount,
            paymentCurrencyCode,
            request.PaymentExchangeRate,
            request.PaymentDate,
            request.CashAccountCode,
            "system:business-erp",
            allocationLines.Select(x => new PaymentExecutionAllocationDraft(x.PayableNo, x.Amount)).ToArray()));
        return allocation.Code;
    }
}

public sealed record ExecutePaymentExecutionCommand(
    string OrganizationId,
    string EnvironmentId,
    string PaymentExecutionNo,
    string ExecutedBy = "system:business-erp") : ICommand;

public sealed class ExecutePaymentExecutionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ExecutePaymentExecutionCommand>
{
    public async Task Handle(ExecutePaymentExecutionCommand request, CancellationToken cancellationToken)
    {
        var paymentExecution = await dbContext.PaymentExecutions
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PaymentExecutionNo == request.PaymentExecutionNo,
                cancellationToken)
            ?? throw new KnownException($"Payment execution '{request.PaymentExecutionNo}' was not found.");
        if (paymentExecution.Status == PaymentExecutionStatus.Executed)
        {
            return;
        }

        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            paymentExecution.PaymentDate,
            "payment execution voucher",
            cancellationToken);
        var allocationLines = paymentExecution.Allocations
            .Select(x => new PayablePaymentAllocationCommandLine(x.PayableNo, x.Amount))
            .ToArray();
        var voucherAllocations = await PaymentExecutionCommandFacts.LoadPayableVoucherAllocationsAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            allocationLines,
            paymentExecution.Amount,
            cancellationToken);
        var supplierCode = PaymentExecutionCommandFacts.ResolveSingleSupplierCode(voucherAllocations);
        if (!string.Equals(supplierCode, paymentExecution.SupplierCode, StringComparison.Ordinal))
        {
            throw new KnownException($"Payment execution '{request.PaymentExecutionNo}' supplier does not match its payable allocations.");
        }

        foreach (var voucherAllocation in voucherAllocations)
        {
            voucherAllocation.Payable.RegisterPayment(voucherAllocation.Amount);
        }

        paymentExecution.Execute(request.ExecutedBy);
        if (!await dbContext.JournalVouchers.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.VoucherNo == paymentExecution.PaymentExecutionNo,
            cancellationToken))
        {
            dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForPayablePayment(
                voucherAllocations,
                paymentExecution.PaymentExecutionNo,
                paymentExecution.Amount,
                paymentExecution.CurrencyCode,
                paymentExecution.PaymentExchangeRate,
                paymentExecution.PaymentDate,
                paymentExecution.CashAccountCode));
        }
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
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.CollectionDate,
            "cash receipt voucher",
            cancellationToken);
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
        var cashReceipt = CashReceipt.Register(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            receivable.CustomerCode,
            request.Amount,
            receivable.CurrencyCode,
            request.CollectionDate,
            request.CashAccountCode,
            [new CashReceiptAllocationDraft(receivable.ReceivableNo, request.Amount)]);
        cashReceipt.Match();
        dbContext.CashReceipts.Add(cashReceipt);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForReceivableCollection(receivable, allocation.Code, request.Amount, request.CollectionDate, request.CashAccountCode));
    }
}

public sealed record RegisterCashReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReceivableNo,
    decimal Amount,
    DateOnly CollectionDate,
    string CashAccountCode,
    string IdempotencyKey) : ICommand<string>;

public sealed class RegisterCashReceiptCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RegisterCashReceiptCommand, string>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<string> Handle(RegisterCashReceiptCommand request, CancellationToken cancellationToken)
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
            && await dbContext.CashReceipts.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.CashReceiptNo == allocation.Code,
                cancellationToken))
        {
            return allocation.Code;
        }

        var receivable = await dbContext.AccountReceivables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ReceivableNo == request.ReceivableNo,
            cancellationToken)
            ?? throw new KnownException($"Account receivable '{request.ReceivableNo}' was not found.");

        dbContext.CashReceipts.Add(CashReceipt.Register(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            receivable.CustomerCode,
            request.Amount,
            receivable.CurrencyCode,
            request.CollectionDate,
            request.CashAccountCode,
            [new CashReceiptAllocationDraft(receivable.ReceivableNo, request.Amount)]));
        return allocation.Code;
    }
}

public sealed record MatchCashReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string CashReceiptNo) : ICommand;

public sealed class MatchCashReceiptCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MatchCashReceiptCommand>
{
    public async Task Handle(MatchCashReceiptCommand request, CancellationToken cancellationToken)
    {
        var cashReceipt = await dbContext.CashReceipts
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CashReceiptNo == request.CashReceiptNo,
                cancellationToken)
            ?? throw new KnownException($"Cash receipt '{request.CashReceiptNo}' was not found.");
        if (cashReceipt.Status == CashReceiptStatus.Matched)
        {
            return;
        }

        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            cashReceipt.ReceiptDate,
            "cash receipt voucher",
            cancellationToken);
        if (cashReceipt.Allocations.Count != 1)
        {
            throw new KnownException($"Cash receipt '{request.CashReceiptNo}' must have exactly one receivable allocation before matching.");
        }

        var allocation = cashReceipt.Allocations.Single();
        var receivable = await dbContext.AccountReceivables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ReceivableNo == allocation.ReceivableNo,
            cancellationToken)
            ?? throw new KnownException($"Account receivable '{allocation.ReceivableNo}' was not found.");

        receivable.RegisterCollection(allocation.Amount);
        cashReceipt.Match();
        if (!await dbContext.JournalVouchers.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.VoucherNo == cashReceipt.CashReceiptNo,
            cancellationToken))
        {
            dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForReceivableCollection(
                receivable,
                cashReceipt.CashReceiptNo,
                allocation.Amount,
                cashReceipt.ReceiptDate,
                cashReceipt.CashAccountCode));
        }
    }
}

public sealed record JournalVoucherCommandLine(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    string Memo,
    string CurrencyCode = "CNY",
    decimal ExchangeRate = 1m,
    decimal? LocalDebitAmount = null,
    decimal? LocalCreditAmount = null);

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
            line.RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
            line.RuleFor(x => x.ExchangeRate).GreaterThan(0);
            line.RuleFor(x => x)
                .Must(x => (x.DebitAmount > 0 && x.CreditAmount == 0) || (x.CreditAmount > 0 && x.DebitAmount == 0))
                .WithMessage("Voucher lines must have exactly one non-zero debit or credit amount.");
        });
        RuleFor(x => x.Lines)
            .Must(x => x.Sum(line => line.LocalDebitAmount ?? line.DebitAmount * line.ExchangeRate) == x.Sum(line => line.LocalCreditAmount ?? line.CreditAmount * line.ExchangeRate))
            .WithMessage("Journal voucher local debits must equal local credits.");
    }
}

public sealed class PostJournalVoucherCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<PostJournalVoucherCommand, JournalVoucherId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<JournalVoucherId> Handle(PostJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.PostingDate,
            "journal voucher",
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "journal-voucher", request.VoucherNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.PostingDate, request.Lines.Select(x => $"{x.AccountCode}:{x.DebitAmount}:{x.CreditAmount}:{x.Memo}:{x.CurrencyCode}:{x.ExchangeRate}:{x.LocalDebitAmount}:{x.LocalCreditAmount}")), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.JournalVouchers.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.VoucherNo == allocation.Code, cancellationToken)).Id;
        }

        var voucher = JournalVoucher.Post(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.PostingDate,
            request.Lines.Select(x => new JournalVoucherLineDraft(x.AccountCode, x.DebitAmount, x.CreditAmount, x.Memo, x.CurrencyCode, x.ExchangeRate, x.LocalDebitAmount, x.LocalCreditAmount)));
        dbContext.JournalVouchers.Add(voucher);
        return voucher.Id;
    }
}

public sealed record PayablePaymentVoucherAllocation(AccountPayable Payable, decimal Amount);

internal static class PaymentExecutionCommandFacts
{
    public static IReadOnlyCollection<PayablePaymentAllocationCommandLine> NormalizeAllocationLines(
        string payableNo,
        decimal amount,
        IReadOnlyCollection<PayablePaymentAllocationCommandLine>? allocations)
    {
        return allocations is { Count: > 0 }
            ? allocations
            : [new PayablePaymentAllocationCommandLine(payableNo, amount)];
    }

    public static async Task<List<PayablePaymentVoucherAllocation>> LoadPayableVoucherAllocationsAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<PayablePaymentAllocationCommandLine> allocationLines,
        decimal paymentAmount,
        CancellationToken cancellationToken)
    {
        var allocatedAmount = allocationLines.Sum(x => x.Amount);
        if (allocatedAmount > paymentAmount)
        {
            throw new KnownException("Allocated payment amount cannot exceed cash payment amount.");
        }

        var payableNos = allocationLines.Select(x => x.PayableNo).Distinct(StringComparer.Ordinal).ToArray();
        var payables = await dbContext.AccountPayables
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && payableNos.Contains(x.PayableNo))
            .ToDictionaryAsync(x => x.PayableNo, StringComparer.Ordinal, cancellationToken);
        var voucherAllocations = new List<PayablePaymentVoucherAllocation>();
        foreach (var line in allocationLines)
        {
            if (!payables.TryGetValue(line.PayableNo, out var payable))
            {
                throw new KnownException($"Account payable '{line.PayableNo}' was not found.");
            }

            voucherAllocations.Add(new PayablePaymentVoucherAllocation(payable, line.Amount));
        }

        return voucherAllocations;
    }

    public static string ResolveSingleSupplierCode(IReadOnlyCollection<PayablePaymentVoucherAllocation> voucherAllocations)
    {
        var supplierCodes = voucherAllocations
            .Select(x => x.Payable.SupplierCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (supplierCodes.Length != 1)
        {
            throw new KnownException("A payment execution can only settle payables from one supplier. Split cross-supplier payments into separate executions.");
        }

        return supplierCodes[0];
    }
}

public static class FinanceVoucherFactory
{
    public const string InventoryAccountCode = "1401";
    public const string AccountsPayableAccountCode = "2202";
    public const string DirectPayableExpenseAccountCode = "5001";
    public const string GoodsReceiptInvoiceReceiptAccountCode = "GR-IR";
    public const string RealizedExchangeLossAccountCode = "6603";
    public const string RealizedExchangeGainAccountCode = "6604";
    public const string OnAccountPrepaymentAccountCode = "1123";

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
                LocalDebit(InventoryAccountCode, amount, receipt.CurrencyCode, receipt.ExchangeRate, $"Goods receipt {receipt.PurchaseReceiptNo}"),
                LocalCredit(GoodsReceiptInvoiceReceiptAccountCode, amount, receipt.CurrencyCode, receipt.ExchangeRate, $"GR/IR accrual {receipt.PurchaseReceiptNo}"),
            ]);
    }

    public static JournalVoucher ForSupplierInvoiceGrIrClearing(SupplierInvoice invoice, AccountPayable payable, decimal grIrExchangeRate)
    {
        if (grIrExchangeRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grIrExchangeRate), grIrExchangeRate, "GR/IR exchange rate must be positive.");
        }

        var lines = new List<JournalVoucherLineDraft>
        {
            LocalDebit(GoodsReceiptInvoiceReceiptAccountCode, invoice.TotalAmount, invoice.CurrencyCode, grIrExchangeRate, $"Clear GR/IR for receipt {invoice.PurchaseReceiptNo}"),
            LocalCredit(AccountsPayableAccountCode, payable.Amount, payable.CurrencyCode, payable.ExchangeRate, $"AP {payable.PayableNo}"),
        };
        var grIrLocalDebit = invoice.TotalAmount * grIrExchangeRate;
        var payableLocalCredit = payable.Amount * payable.ExchangeRate;
        var exchangeDifference = payableLocalCredit - grIrLocalDebit;
        if (exchangeDifference > 0)
        {
            lines.Add(LocalDebit(RealizedExchangeLossAccountCode, exchangeDifference, "CNY", 1m, "Realized GR/IR exchange loss"));
        }
        else if (exchangeDifference < 0)
        {
            lines.Add(LocalCredit(RealizedExchangeGainAccountCode, Math.Abs(exchangeDifference), "CNY", 1m, "Realized GR/IR exchange gain"));
        }

        return JournalVoucher.Post(
            invoice.OrganizationId,
            invoice.EnvironmentId,
            $"JV-AP-{payable.PayableNo}",
            invoice.InvoiceDate,
            lines);
    }

    public static JournalVoucher ForAccountPayable(AccountPayable payable)
    {
        return JournalVoucher.Post(
            payable.OrganizationId,
            payable.EnvironmentId,
            $"JV-AP-{payable.PayableNo}",
            payable.InvoiceDate,
            [
                LocalDebit(DirectPayableExpenseAccountCode, payable.Amount, payable.CurrencyCode, payable.ExchangeRate, $"Direct AP expense {payable.SourceDocumentNo}"),
                LocalCredit(AccountsPayableAccountCode, payable.Amount, payable.CurrencyCode, payable.ExchangeRate, $"AP {payable.PayableNo}"),
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
                LocalDebit("1122", receivable.Amount, receivable.CurrencyCode, receivable.ExchangeRate, $"AR {receivable.ReceivableNo}"),
                LocalCredit("6001", receivable.Amount, receivable.CurrencyCode, receivable.ExchangeRate, $"AR source {receivable.SourceDocumentNo}"),
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
                LocalDebit("5001", candidate.Amount, candidate.CurrencyCode, candidate.ExchangeRate, $"Cost candidate {candidate.SourceDocumentNo}"),
                LocalCredit("1401", candidate.Amount, candidate.CurrencyCode, candidate.ExchangeRate, $"Cost source {candidate.SourceType}"),
            ]);
    }

    public static JournalVoucher ForPayablePayment(AccountPayable payable, string voucherNo, decimal amount, DateOnly paymentDate, string cashAccountCode)
    {
        return ForPayablePayment(
            [new PayablePaymentVoucherAllocation(payable, amount)],
            voucherNo,
            amount,
            payable.CurrencyCode,
            payable.ExchangeRate,
            paymentDate,
            cashAccountCode);
    }

    public static JournalVoucher ForPayablePayment(
        IReadOnlyCollection<PayablePaymentVoucherAllocation> allocations,
        string voucherNo,
        decimal paymentAmount,
        string paymentCurrencyCode,
        decimal paymentExchangeRate,
        DateOnly paymentDate,
        string cashAccountCode)
    {
        if (allocations.Count == 0)
        {
            throw new ArgumentException("At least one payable allocation is required.", nameof(allocations));
        }

        var normalizedPaymentCurrencyCode = string.IsNullOrWhiteSpace(paymentCurrencyCode)
            ? throw new ArgumentException("Payment currency code is required.", nameof(paymentCurrencyCode))
            : paymentCurrencyCode.Trim().ToUpperInvariant();
        if (paymentAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentAmount), paymentAmount, "Payment amount must be positive.");
        }

        if (paymentExchangeRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentExchangeRate), paymentExchangeRate, "Payment exchange rate must be positive.");
        }
        var lines = new List<JournalVoucherLineDraft>();
        var allocatedAmount = 0m;
        var localDebitAmount = 0m;
        foreach (var allocation in allocations)
        {
            allocatedAmount += allocation.Amount;
            var localAmount = allocation.Amount * allocation.Payable.ExchangeRate;
            localDebitAmount += localAmount;
            lines.Add(new JournalVoucherLineDraft(
                AccountsPayableAccountCode,
                allocation.Amount,
                0m,
                $"Pay AP {allocation.Payable.PayableNo}",
                allocation.Payable.CurrencyCode,
                allocation.Payable.ExchangeRate,
                localAmount,
                null));
        }

        if (allocatedAmount > paymentAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentAmount), paymentAmount, "Payment amount cannot be less than allocated payable amount.");
        }

        var onAccountAmount = paymentAmount - allocatedAmount;
        if (onAccountAmount > 0)
        {
            var localOnAccountAmount = onAccountAmount * paymentExchangeRate;
            localDebitAmount += localOnAccountAmount;
            lines.Add(new JournalVoucherLineDraft(
                OnAccountPrepaymentAccountCode,
                onAccountAmount,
                0m,
                "On-account supplier prepayment",
                normalizedPaymentCurrencyCode,
                paymentExchangeRate,
                localOnAccountAmount,
                null));
        }

        var localCreditAmount = paymentAmount * paymentExchangeRate;
        var exchangeDifference = localCreditAmount - localDebitAmount;
        if (exchangeDifference > 0)
        {
            lines.Add(LocalDebit(RealizedExchangeLossAccountCode, exchangeDifference, "CNY", 1m, "Realized exchange loss"));
            localDebitAmount += exchangeDifference;
        }
        else if (exchangeDifference < 0)
        {
            lines.Add(LocalCredit(RealizedExchangeGainAccountCode, Math.Abs(exchangeDifference), "CNY", 1m, "Realized exchange gain"));
        }

        lines.Add(new JournalVoucherLineDraft(
            cashAccountCode,
            0m,
            paymentAmount,
            $"Cash payment {voucherNo}",
            normalizedPaymentCurrencyCode,
            paymentExchangeRate,
            null,
            localCreditAmount));

        return JournalVoucher.Post(
            allocations.First().Payable.OrganizationId,
            allocations.First().Payable.EnvironmentId,
            voucherNo,
            paymentDate,
            lines);
    }

    public static JournalVoucher ForReceivableCollection(AccountReceivable receivable, string voucherNo, decimal amount, DateOnly collectionDate, string cashAccountCode)
    {
        return JournalVoucher.Post(
            receivable.OrganizationId,
            receivable.EnvironmentId,
            voucherNo,
            collectionDate,
            [
                LocalDebit(cashAccountCode, amount, receivable.CurrencyCode, receivable.ExchangeRate, $"Cash collection for {receivable.ReceivableNo}"),
                LocalCredit("1122", amount, receivable.CurrencyCode, receivable.ExchangeRate, $"Collect AR {receivable.ReceivableNo}"),
            ]);
    }

    private static JournalVoucherLineDraft LocalDebit(string accountCode, decimal amount, string currencyCode, decimal exchangeRate, string memo)
    {
        return new JournalVoucherLineDraft(accountCode, amount, 0m, memo, currencyCode, exchangeRate, amount * exchangeRate, null);
    }

    private static JournalVoucherLineDraft LocalCredit(string accountCode, decimal amount, string currencyCode, decimal exchangeRate, string memo)
    {
        return new JournalVoucherLineDraft(accountCode, 0m, amount, memo, currencyCode, exchangeRate, null, amount * exchangeRate);
    }
}
