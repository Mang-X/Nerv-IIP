using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Validation;

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

public sealed class CloseAccountingPeriodCommandHandler(
    ApplicationDbContext dbContext,
    IErpAdvisoryLockAllocator reconciliationLock)
    : ICommandHandler<CloseAccountingPeriodCommand>
{
    public async Task Handle(CloseAccountingPeriodCommand request, CancellationToken cancellationToken)
    {
        await reconciliationLock.AcquireAsync(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadReconciliation,
            request.OrganizationId.Trim(), request.EnvironmentId.Trim(), request.PeriodCode.Trim(),
            cancellationToken);
        var period = await AccountingPeriodPostingGuard.FindPeriodAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.PeriodCode, cancellationToken);
        await MachineOverheadPeriodCloseGuard.EnsureReadyAsync(
            dbContext, reconciliationLock,
            request.OrganizationId, request.EnvironmentId, request.PeriodCode,
            cancellationToken);
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
            ?? throw new KnownException($"会计期间『{periodCode}』不存在。");
    }

    public static async Task EnsureOpenAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        DateOnly postingDate,
        string _sourceDescription,
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

        throw new KnownException($"会计期间『{period.PeriodCode}』已关闭，请先重开期间。");
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
        // 来源单据与供应商必须真实存在且一致，否则财务账可凭空生成垃圾应付单。
        var authoritativeSupplierCode = await AccountPayableSourceDocumentGuard.EnsureSourceDocumentAndSupplierAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceDocumentNo,
            request.SupplierCode,
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-payable", request.PayableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountPayables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.PayableNo == allocation.Code, cancellationToken)).Id;
        }

        var payable = AccountPayable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, authoritativeSupplierCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate);
        dbContext.AccountPayables.Add(payable);
        // #3278 / S6：凭证号改取 journal-voucher 规则短号；来源身份仍是应付单，
        // 取号幂等键也用它，故重放拿回同一个号。
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForAccountPayable(
            payable,
            await JournalVoucherNoAllocation.AllocateAsync(
                _codingService,
                request.OrganizationId,
                request.EnvironmentId,
                JournalVoucherSourceType.AccountPayable,
                payable.PayableNo,
                cancellationToken)));
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
        // 来源单据与客户必须真实存在且一致，否则财务账可凭空生成垃圾应收单。
        var authoritativeCustomerCode = await AccountReceivableSourceDocumentGuard.EnsureSourceDocumentAndCustomerAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceDocumentNo,
            request.CustomerCode,
            cancellationToken);
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-receivable", request.ReceivableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountReceivables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.ReceivableNo == allocation.Code, cancellationToken)).Id;
        }

        var receivable = AccountReceivable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, authoritativeCustomerCode, request.Amount, request.CurrencyCode, request.InvoiceDate, request.DueDate, request.PaymentTermCode, request.ExchangeRate);
        dbContext.AccountReceivables.Add(receivable);
        // #3278 / S6：同 CreateAccountPayable，凭证号改取短号，来源身份仍是应收单。
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForAccountReceivable(
            receivable,
            await JournalVoucherNoAllocation.AllocateAsync(
                _codingService,
                request.OrganizationId,
                request.EnvironmentId,
                JournalVoucherSourceType.AccountReceivable,
                receivable.ReceivableNo,
                cancellationToken)));
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
        // #3278 / S6：同上，凭证号改取短号，来源身份仍是成本待定档。
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForCostCandidate(
            candidate,
            await JournalVoucherNoAllocation.AllocateAsync(
                _codingService,
                request.OrganizationId,
                request.EnvironmentId,
                JournalVoucherSourceType.CostCandidate,
                candidate.CandidateNo,
                cancellationToken)));
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
        // #3278 / S5：查重键从凭证号搬到来源两列。
        // `allocation.Code` 在这条路径上同时被写成 PaymentExecution.PaymentExecutionNo 和
        // JournalVoucher.VoucherNo（见本方法末尾），但这里取的是**付款执行单号**这一身份：
        // S6 把凭证号换成分配器短号后，来源列不跟着变，这条查重也就不会跟着失效。
        var paymentExecutionSourceType = JournalVoucherSourceType.PaymentExecution.Code;
        var paymentExecutionSourceNo = allocation.Code;
        if (allocation.IsIdempotentReplay
            && await dbContext.JournalVouchers.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.SourceType == paymentExecutionSourceType
                    && x.SourceNo == paymentExecutionSourceNo,
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
        // #3278 / S6：凭证号改取 journal-voucher 规则短号，**不再**复用付款执行单号。
        // 取号幂等键用的是本方法上面那条查重谓词的同一对来源值（APPAY, 付款执行单号），
        // 于是与 ExecutePaymentExecution 那条入口天然认同一个号。
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForPayablePayment(
            voucherAllocations,
            await JournalVoucherNoAllocation.AllocateAsync(
                _codingService,
                request.OrganizationId,
                request.EnvironmentId,
                JournalVoucherSourceType.PaymentExecution,
                paymentExecutionSourceNo,
                cancellationToken),
            paymentExecution.PaymentExecutionNo,
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

public sealed class ExecutePaymentExecutionCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<ExecutePaymentExecutionCommand>
{
    // #3278 / S6：本 handler 改前不取号（凭证号直接复用付款执行单号），换短号后需要分配器。
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task Handle(ExecutePaymentExecutionCommand request, CancellationToken cancellationToken)
    {
        var paymentExecution = await dbContext.PaymentExecutions
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PaymentExecutionNo == request.PaymentExecutionNo,
                cancellationToken)
            ?? throw new KnownException($"付款执行『{request.PaymentExecutionNo}』不存在。");
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
            throw new KnownException($"付款执行『{request.PaymentExecutionNo}』供应商与应付分配不符。");
        }

        foreach (var voucherAllocation in voucherAllocations)
        {
            voucherAllocation.Payable.RegisterPayment(voucherAllocation.Amount);
        }

        paymentExecution.Execute(request.ExecutedBy);
        // #3278 / S5：查重键从凭证号搬到来源两列。本位点与 RegisterAccountPayablePayment 是
        // **同一张付款凭证的两条入口**（批准即执行 / 先批准后执行），两边必须认同一个来源身份，
        // 否则换号后这两条入口会各记一张。
        var paymentExecutionSourceType = JournalVoucherSourceType.PaymentExecution.Code;
        var paymentExecutionSourceNo = paymentExecution.PaymentExecutionNo;
        if (!await dbContext.JournalVouchers.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceType == paymentExecutionSourceType
                && x.SourceNo == paymentExecutionSourceNo,
            cancellationToken))
        {
            // #3278 / S6：凭证号改取短号。这条入口与 RegisterAccountPayablePayment 用**同一对**
            // 来源值取号 ⇒ 同一张付款执行无论走哪条入口都拿回同一个凭证号，
            // 而「只记一张」由上面那条来源列谓词 + S5 的 partial unique index 承担。
            dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForPayablePayment(
                voucherAllocations,
                await JournalVoucherNoAllocation.AllocateAsync(
                    _codingService,
                    request.OrganizationId,
                    request.EnvironmentId,
                    JournalVoucherSourceType.PaymentExecution,
                    paymentExecutionSourceNo,
                    cancellationToken),
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
        // #3278 / S5：查重键从凭证号搬到来源两列。
        // `allocation.Code` 在这条路径上同时被写成 CashReceipt.CashReceiptNo 和 JournalVoucher.VoucherNo；
        // 这里取的是**收款单号**这一身份，理由同 RegisterAccountPayablePayment。
        var cashReceiptSourceType = JournalVoucherSourceType.CashReceipt.Code;
        var cashReceiptSourceNo = allocation.Code;
        if (allocation.IsIdempotentReplay
            && await dbContext.JournalVouchers.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.SourceType == cashReceiptSourceType
                    && x.SourceNo == cashReceiptSourceNo,
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
        // #3278 / S6：凭证号改取短号，**不再**复用收款单号；取号键同本方法上面那条查重谓词。
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForReceivableCollection(
            receivable,
            await JournalVoucherNoAllocation.AllocateAsync(
                _codingService,
                request.OrganizationId,
                request.EnvironmentId,
                JournalVoucherSourceType.CashReceipt,
                cashReceiptSourceNo,
                cancellationToken),
            cashReceipt.CashReceiptNo,
            request.Amount,
            request.CollectionDate,
            request.CashAccountCode));
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
            ?? throw new KnownException($"应收单『{request.ReceivableNo}』不存在。");

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

public sealed class MatchCashReceiptCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<MatchCashReceiptCommand>
{
    // #3278 / S6：本 handler 改前不取号（凭证号直接复用收款单号），换短号后需要分配器。
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task Handle(MatchCashReceiptCommand request, CancellationToken cancellationToken)
    {
        var cashReceipt = await dbContext.CashReceipts
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CashReceiptNo == request.CashReceiptNo,
                cancellationToken)
            ?? throw new KnownException($"收款单『{request.CashReceiptNo}』不存在。");
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
            throw new KnownException($"收款单『{request.CashReceiptNo}』必须只有一条应收分配。");
        }

        var allocation = cashReceipt.Allocations.Single();
        var receivable = await dbContext.AccountReceivables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ReceivableNo == allocation.ReceivableNo,
            cancellationToken)
            ?? throw new KnownException($"应收单『{allocation.ReceivableNo}』不存在。");

        receivable.RegisterCollection(allocation.Amount);
        cashReceipt.Match();
        // #3278 / S5：查重键从凭证号搬到来源两列。本位点与 RegisterAccountReceivableCollection 是
        // 同一张收款凭证的两条入口（登记即匹配 / 先登记后匹配），理由同付款那一对。
        var cashReceiptSourceType = JournalVoucherSourceType.CashReceipt.Code;
        var cashReceiptSourceNo = cashReceipt.CashReceiptNo;
        if (!await dbContext.JournalVouchers.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceType == cashReceiptSourceType
                && x.SourceNo == cashReceiptSourceNo,
            cancellationToken))
        {
            // #3278 / S6：凭证号改取短号。与 RegisterAccountReceivableCollection 用同一对来源值取号，
            // 理由同付款那一对。
            dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForReceivableCollection(
                receivable,
                await JournalVoucherNoAllocation.AllocateAsync(
                    _codingService,
                    request.OrganizationId,
                    request.EnvironmentId,
                    JournalVoucherSourceType.CashReceipt,
                    cashReceiptSourceNo,
                    cancellationToken),
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
        RuleFor(x => x.VoucherNo).MaximumLength(ErpVoucherNoPolicy.ColumnMaxLength);
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
            // #3278 / S5 **刻意不改**这一处：手工凭证的来源单号就是凭证号自身
            // （`JournalVoucherSourceType.Manual`，见本方法下面的 Post 调用），
            // 所以 `VoucherNo == allocation.Code` 与 `(MANUAL, allocation.Code)` 在本路径上是同一个谓词。
            // 这也是母票「显式不做」里点名的唯一一处按凭证号定位的 A 族位点。
            return (await dbContext.JournalVouchers.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.VoucherNo == allocation.Code, cancellationToken)).Id;
        }

        var voucher = JournalVoucher.Post(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.PostingDate,
            request.Lines.Select(x => new JournalVoucherLineDraft(x.AccountCode, x.DebitAmount, x.CreditAmount, x.Memo, x.CurrencyCode, x.ExchangeRate, x.LocalDebitAmount, x.LocalCreditAmount)),
            // 手工凭证没有上游单据，来源单号取凭证号自身（见 JournalVoucherSourceType.Manual）。
            JournalVoucherSourceType.Manual,
            allocation.Code);
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
            throw new KnownException("分配付款金额不能超过付款金额。");
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
                throw new KnownException($"应付单『{line.PayableNo}』不存在。");
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
            throw new KnownException("一次付款只能结算同一供应商的应付单。");
        }

        return supplierCodes[0];
    }
}

public static class FinanceVoucherFactory
{
    public const string InventoryAccountCode = "1401";
    public const string AccountsPayableAccountCode = "2202";
    public const string AccountsReceivableAccountCode = "1122";
    public const string SalesReturnsAccountCode = "6001";
    public const string DirectPayableExpenseAccountCode = "5001";
    public const string GoodsReceiptInvoiceReceiptAccountCode = "GR-IR";
    public const string RealizedExchangeLossAccountCode = "6603";
    public const string RealizedExchangeGainAccountCode = "6604";
    public const string OnAccountPrepaymentAccountCode = "1123";

    public static string GoodsReceiptIrAccrualVoucherNo(string purchaseReceiptNo)
    {
        return ErpVoucherNoPolicy.Compose(VoucherFamily.GoodsReceiptIrAccrual, purchaseReceiptNo);
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
            ],
            JournalVoucherSourceType.GoodsReceiptIrAccrual,
            receipt.PurchaseReceiptNo);
    }

    public static JournalVoucher ForPurchaseReturn(PurchaseReturn purchaseReturn, string voucherNo, DateOnly postingDate)
    {
        var lines = new List<JournalVoucherLineDraft>();
        if (purchaseReturn.GrIrReversalAmount > 0m)
        {
            lines.Add(LocalDebit(GoodsReceiptInvoiceReceiptAccountCode, purchaseReturn.GrIrReversalAmount, purchaseReturn.CurrencyCode, purchaseReturn.ExchangeRate, $"Reverse GR/IR {purchaseReturn.PurchaseReturnNo}"));
        }

        if (purchaseReturn.DebitNoteAmount > 0m)
        {
            lines.Add(LocalDebit(AccountsPayableAccountCode, purchaseReturn.DebitNoteAmount, purchaseReturn.CurrencyCode, purchaseReturn.ExchangeRate, $"Debit note {purchaseReturn.PurchaseReturnNo}"));
        }

        lines.Add(LocalCredit(InventoryAccountCode, purchaseReturn.TotalAmount, purchaseReturn.CurrencyCode, purchaseReturn.ExchangeRate, $"Supplier return {purchaseReturn.PurchaseReturnNo}"));
        return JournalVoucher.Post(
            purchaseReturn.OrganizationId,
            purchaseReturn.EnvironmentId,
            voucherNo,
            postingDate,
            lines,
            JournalVoucherSourceType.PurchaseReturn,
            purchaseReturn.PurchaseReturnNo);
    }

    public static JournalVoucher ForCreditNote(CreditNote creditNote, DateOnly postingDate)
    {
        return JournalVoucher.Post(
            creditNote.OrganizationId,
            creditNote.EnvironmentId,
            ErpVoucherNoPolicy.Compose(VoucherFamily.CreditNote, creditNote.CreditNoteNo),
            postingDate,
            [
                LocalDebit(SalesReturnsAccountCode, creditNote.Amount, creditNote.CurrencyCode, creditNote.ExchangeRate, $"Credit note {creditNote.CreditNoteNo}"),
                LocalCredit(AccountsReceivableAccountCode, creditNote.Amount, creditNote.CurrencyCode, creditNote.ExchangeRate, $"Settle AR {creditNote.AccountReceivableNo}"),
            ],
            JournalVoucherSourceType.CreditNote,
            creditNote.CreditNoteNo);
    }

    /// <remarks>
    /// #3278 / S6：<paramref name="voucherNo"/> 由调用方从 <see cref="JournalVoucherNoAllocation"/> 取，
    /// **不再**由本方法 <c>Compose(AccountPayable, payable.PayableNo)</c> 派生。
    /// 改前它与 <see cref="ForAccountPayable"/> 产出同一个 <c>JV-AP-{应付单号}</c>（#3278 §A2 的设计地雷）；
    /// 改后两条路径各取一个短号，而来源列仍按各自的驱动单据取值
    /// （本方法是供应商发票，<see cref="ForAccountPayable"/> 是应付单）。
    /// </remarks>
    public static JournalVoucher ForSupplierInvoiceGrIrClearing(SupplierInvoice invoice, AccountPayable payable, decimal grIrExchangeRate, string voucherNo)
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
            voucherNo,
            invoice.InvoiceDate,
            lines,
            // 来源列按**驱动这张凭证的单据**取值：这里是供应商发票，不是应付单。
            // 改号前两条路径还会撞出同一个凭证号（#3278 §A2），S2 靠这一对来源值把它们分开；
            // S6 换短号后凭证号也不再相撞，但来源取值的理由不变——⛔ 别因为撞号没了就把它合并到 AP 族。
            JournalVoucherSourceType.SupplierInvoice,
            invoice.InvoiceNo);
    }

    /// <remarks>
    /// #3278 / S6：<paramref name="voucherNo"/> 由调用方从 <see cref="JournalVoucherNoAllocation"/> 取。
    /// </remarks>
    public static JournalVoucher ForAccountPayable(AccountPayable payable, string voucherNo)
    {
        return JournalVoucher.Post(
            payable.OrganizationId,
            payable.EnvironmentId,
            voucherNo,
            payable.InvoiceDate,
            [
                LocalDebit(DirectPayableExpenseAccountCode, payable.Amount, payable.CurrencyCode, payable.ExchangeRate, $"Direct AP expense {payable.SourceDocumentNo}"),
                LocalCredit(AccountsPayableAccountCode, payable.Amount, payable.CurrencyCode, payable.ExchangeRate, $"AP {payable.PayableNo}"),
            ],
            JournalVoucherSourceType.AccountPayable,
            payable.PayableNo);
    }

    /// <remarks>
    /// #3278 / S6：<paramref name="voucherNo"/> 由调用方从 <see cref="JournalVoucherNoAllocation"/> 取。
    /// </remarks>
    public static JournalVoucher ForAccountReceivable(AccountReceivable receivable, string voucherNo)
    {
        return JournalVoucher.Post(
            receivable.OrganizationId,
            receivable.EnvironmentId,
            voucherNo,
            receivable.InvoiceDate,
            [
                LocalDebit(AccountsReceivableAccountCode, receivable.Amount, receivable.CurrencyCode, receivable.ExchangeRate, $"AR {receivable.ReceivableNo}"),
                LocalCredit(SalesReturnsAccountCode, receivable.Amount, receivable.CurrencyCode, receivable.ExchangeRate, $"AR source {receivable.SourceDocumentNo}"),
            ],
            JournalVoucherSourceType.AccountReceivable,
            receivable.ReceivableNo);
    }

    /// <remarks>
    /// #3278 / S6：<paramref name="voucherNo"/> 由调用方从 <see cref="JournalVoucherNoAllocation"/> 取。
    /// </remarks>
    public static JournalVoucher ForCostCandidate(CostCandidate candidate, string voucherNo)
    {
        return JournalVoucher.Post(
            candidate.OrganizationId,
            candidate.EnvironmentId,
            voucherNo,
            DateOnly.FromDateTime(candidate.CreatedAtUtc),
            [
                LocalDebit("5001", candidate.Amount, candidate.CurrencyCode, candidate.ExchangeRate, $"Cost candidate {candidate.SourceDocumentNo}"),
                LocalCredit("1401", candidate.Amount, candidate.CurrencyCode, candidate.ExchangeRate, $"Cost source {candidate.SourceType}"),
            ],
            JournalVoucherSourceType.CostCandidate,
            candidate.CandidateNo);
    }

    public static JournalVoucher ForPayablePayment(AccountPayable payable, string voucherNo, string paymentExecutionNo, decimal amount, DateOnly paymentDate, string cashAccountCode)
    {
        return ForPayablePayment(
            [new PayablePaymentVoucherAllocation(payable, amount)],
            voucherNo,
            paymentExecutionNo,
            amount,
            payable.CurrencyCode,
            payable.ExchangeRate,
            paymentDate,
            cashAccountCode);
    }

    /// <remarks>
    /// <paramref name="paymentExecutionNo"/> 与 <paramref name="voucherNo"/> **取值已不再相同**：
    /// #3278 / S6 落地后凭证号是 <c>journal-voucher</c> 规则的分配器短号
    /// （见 <see cref="JournalVoucherNoAllocation"/>），来源单号仍是付款执行单号。
    /// 两个参数分开正是为了让那次改动不会静默改掉来源身份——⛔ 别再把它们合并回一个参数。
    /// </remarks>
    public static JournalVoucher ForPayablePayment(
        IReadOnlyCollection<PayablePaymentVoucherAllocation> allocations,
        string voucherNo,
        string paymentExecutionNo,
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
            // #3278 / S6：改前这里写的是 voucherNo，而那时 voucherNo 取值恰等于付款执行单号，
            // 所以摘要读起来是「付了哪一笔」。换短号后再回抄 voucherNo 会让摘要变成凭证号自指
            // （「Cash payment JV-20260915-000001」对账时没有任何信息量），故改取付款执行单号——
            // 这是把摘要原本要表达的东西写回来，⛔ 不是换了一个意思。
            $"Cash payment {paymentExecutionNo}",
            normalizedPaymentCurrencyCode,
            paymentExchangeRate,
            null,
            localCreditAmount));

        return JournalVoucher.Post(
            allocations.First().Payable.OrganizationId,
            allocations.First().Payable.EnvironmentId,
            voucherNo,
            paymentDate,
            lines,
            JournalVoucherSourceType.PaymentExecution,
            paymentExecutionNo);
    }

    /// <remarks>
    /// <paramref name="cashReceiptNo"/> 与 <paramref name="voucherNo"/> **取值已不再相同**（#3278 / S6），语义本就不同；
    /// 拆成两个参数的理由同 <see cref="ForPayablePayment(IReadOnlyCollection{PayablePaymentVoucherAllocation}, string, string, decimal, string, decimal, DateOnly, string)"/>。
    /// </remarks>
    public static JournalVoucher ForReceivableCollection(AccountReceivable receivable, string voucherNo, string cashReceiptNo, decimal amount, DateOnly collectionDate, string cashAccountCode)
    {
        return JournalVoucher.Post(
            receivable.OrganizationId,
            receivable.EnvironmentId,
            voucherNo,
            collectionDate,
            [
                LocalDebit(cashAccountCode, amount, receivable.CurrencyCode, receivable.ExchangeRate, $"Cash collection for {receivable.ReceivableNo}"),
                LocalCredit("1122", amount, receivable.CurrencyCode, receivable.ExchangeRate, $"Collect AR {receivable.ReceivableNo}"),
            ],
            JournalVoucherSourceType.CashReceipt,
            cashReceiptNo);
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
