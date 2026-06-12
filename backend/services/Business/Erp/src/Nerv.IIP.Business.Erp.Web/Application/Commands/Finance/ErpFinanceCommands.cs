using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

public sealed record CreateAccountPayableCommand(string OrganizationId, string EnvironmentId, string? PayableNo, string SourceDocumentNo, string SupplierCode, decimal Amount, string CurrencyCode, string? IdempotencyKey = null) : ICommand<AccountPayableId>;

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
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-payable", request.PayableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountPayables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.PayableNo == allocation.Code, cancellationToken)).Id;
        }

        var payable = AccountPayable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, request.SupplierCode, request.Amount, request.CurrencyCode);
        dbContext.AccountPayables.Add(payable);
        return payable.Id;
    }
}

public sealed record CreateAccountReceivableCommand(string OrganizationId, string EnvironmentId, string? ReceivableNo, string SourceDocumentNo, string CustomerCode, decimal Amount, string CurrencyCode, string? IdempotencyKey = null) : ICommand<AccountReceivableId>;

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
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "account-receivable", request.ReceivableNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.AccountReceivables.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.ReceivableNo == allocation.Code, cancellationToken)).Id;
        }

        var receivable = AccountReceivable.Create(request.OrganizationId, request.EnvironmentId, allocation.Code, request.SourceDocumentNo, request.CustomerCode, request.Amount, request.CurrencyCode);
        dbContext.AccountReceivables.Add(receivable);
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
        return candidate.Id;
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
