using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record WithdrawApprovalChainCommand(
    ApprovalChainId ChainId,
    string ActorType,
    string ActorRef,
    string? Reason) : ICommand;

public sealed class WithdrawApprovalChainCommandValidator : AbstractValidator<WithdrawApprovalChainCommand>
{
    public WithdrawApprovalChainCommandValidator()
    {
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.ActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class WithdrawApprovalChainCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<WithdrawApprovalChainCommand>
{
    public async Task Handle(WithdrawApprovalChainCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadChainAsync(dbContext, request.ChainId, cancellationToken);
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => chain.Withdraw(request.ActorType, request.ActorRef, request.Reason, DateTimeOffset.UtcNow));
    }
}

public sealed record ResubmitApprovalChainCommand(
    ApprovalChainId ChainId,
    string ActorType,
    string ActorRef,
    string? Reason) : ICommand;

public sealed class ResubmitApprovalChainCommandValidator : AbstractValidator<ResubmitApprovalChainCommand>
{
    public ResubmitApprovalChainCommandValidator()
    {
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.ActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class ResubmitApprovalChainCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ResubmitApprovalChainCommand>
{
    public async Task Handle(ResubmitApprovalChainCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadChainAsync(dbContext, request.ChainId, cancellationToken);
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => chain.Resubmit(request.ActorType, request.ActorRef, request.Reason, DateTimeOffset.UtcNow));
    }
}

public sealed record AddApprovalStepSignerCommand(
    ApprovalChainId ChainId,
    int StepNo,
    string ApproverType,
    string ApproverRef,
    string RequestedByActorType,
    string RequestedByActorRef,
    string? Reason) : ICommand<ApprovalStepId>;

public sealed class AddApprovalStepSignerCommandValidator : AbstractValidator<AddApprovalStepSignerCommand>
{
    public AddApprovalStepSignerCommandValidator()
    {
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.StepNo).GreaterThan(0);
        RuleFor(x => x.ApproverType).RequiredApprovalCode(50);
        RuleFor(x => x.ApproverRef).RequiredApprovalCode(150);
        RuleFor(x => x.RequestedByActorType).RequiredApprovalCode(50);
        RuleFor(x => x.RequestedByActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class AddApprovalStepSignerCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AddApprovalStepSignerCommand, ApprovalStepId>
{
    public async Task<ApprovalStepId> Handle(AddApprovalStepSignerCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadChainAsync(dbContext, request.ChainId, cancellationToken);
        ApprovalStep step = null!;
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => step = chain.AddSigner(
            request.StepNo,
            request.ApproverType,
            request.ApproverRef,
            request.RequestedByActorType,
            request.RequestedByActorRef,
            request.Reason));
        return step.Id;
    }
}

public sealed record TransferApprovalStepCommand(
    ApprovalChainId ChainId,
    int StepNo,
    string FromActorType,
    string FromActorRef,
    string ToActorType,
    string ToActorRef,
    string RequestedByActorType,
    string RequestedByActorRef,
    string? Reason) : ICommand;

public sealed class TransferApprovalStepCommandValidator : AbstractValidator<TransferApprovalStepCommand>
{
    public TransferApprovalStepCommandValidator()
    {
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.StepNo).GreaterThan(0);
        RuleFor(x => x.FromActorType).RequiredApprovalCode(50);
        RuleFor(x => x.FromActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.ToActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ToActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.RequestedByActorType).RequiredApprovalCode(50);
        RuleFor(x => x.RequestedByActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class TransferApprovalStepCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<TransferApprovalStepCommand>
{
    public async Task Handle(TransferApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadChainAsync(dbContext, request.ChainId, cancellationToken);
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => chain.Transfer(
            request.StepNo,
            request.FromActorType,
            request.FromActorRef,
            request.ToActorType,
            request.ToActorRef,
            request.RequestedByActorType,
            request.RequestedByActorRef,
            request.Reason));
    }
}

file static class ApprovalActionCommandHelpers
{
    public static async Task<ApprovalChain> LoadChainAsync(
        ApplicationDbContext dbContext,
        ApprovalChainId chainId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == chainId, cancellationToken)
            ?? throw new KnownException("Approval chain was not found.");
    }

    public static void ExecuteDomainAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}
