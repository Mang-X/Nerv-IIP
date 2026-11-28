using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record WithdrawApprovalChainCommand(
    string OrganizationId,
    string EnvironmentId,
    ApprovalChainId ChainId,
    string ActorType,
    string ActorRef,
    string? Reason) : ICommand;

public sealed class WithdrawApprovalChainCommandValidator : AbstractValidator<WithdrawApprovalChainCommand>
{
    public WithdrawApprovalChainCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.ActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class WithdrawApprovalChainCommandHandler(ApplicationDbContext dbContext, IApprovalClock clock)
    : ICommandHandler<WithdrawApprovalChainCommand>
{
    public async Task Handle(WithdrawApprovalChainCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadScopedChainAsync(
            dbContext, request.ChainId, request.OrganizationId, request.EnvironmentId, cancellationToken);

        ApprovalActionCommandHelpers.ExecuteDomainAction(() => chain.Withdraw(request.ActorType, request.ActorRef, request.Reason, clock.UtcNow));
    }
}

public sealed record ResubmitApprovalChainCommand(
    string OrganizationId,
    string EnvironmentId,
    ApprovalChainId ChainId,
    string ActorType,
    string ActorRef,
    string? Reason) : ICommand;

public sealed class ResubmitApprovalChainCommandValidator : AbstractValidator<ResubmitApprovalChainCommand>
{
    public ResubmitApprovalChainCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.ActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class ResubmitApprovalChainCommandHandler(ApplicationDbContext dbContext, IApprovalClock clock)
    : ICommandHandler<ResubmitApprovalChainCommand>
{
    public async Task Handle(ResubmitApprovalChainCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadScopedChainAsync(
            dbContext, request.ChainId, request.OrganizationId, request.EnvironmentId, cancellationToken);
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => chain.Resubmit(request.ActorType, request.ActorRef, request.Reason, clock.UtcNow));
    }
}

public sealed record AddApprovalStepSignerCommand(
    string OrganizationId,
    string EnvironmentId,
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
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.StepNo).GreaterThan(0);
        RuleFor(x => x.ApproverType).RequiredApprovalCode(50);
        RuleFor(x => x.ApproverRef).RequiredApprovalCode(150);
        RuleFor(x => x.RequestedByActorType).RequiredApprovalCode(50);
        RuleFor(x => x.RequestedByActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class AddApprovalStepSignerCommandHandler(ApplicationDbContext dbContext, IApprovalClock clock)
    : ICommandHandler<AddApprovalStepSignerCommand, ApprovalStepId>
{
    public async Task<ApprovalStepId> Handle(AddApprovalStepSignerCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadScopedChainAsync(
            dbContext, request.ChainId, request.OrganizationId, request.EnvironmentId, cancellationToken);
        ApprovalStep step = null!;
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => step = chain.AddSigner(
            request.StepNo,
            request.ApproverType,
            request.ApproverRef,
            request.RequestedByActorType,
            request.RequestedByActorRef,
            request.Reason,
            clock.UtcNow));
        return step.Id;
    }
}

public sealed record TransferApprovalStepCommand(
    string OrganizationId,
    string EnvironmentId,
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
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
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

public sealed class TransferApprovalStepCommandHandler(ApplicationDbContext dbContext, IApprovalClock clock)
    : ICommandHandler<TransferApprovalStepCommand>
{
    public async Task Handle(TransferApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var chain = await ApprovalActionCommandHelpers.LoadScopedChainAsync(
            dbContext, request.ChainId, request.OrganizationId, request.EnvironmentId, cancellationToken);
        ApprovalActionCommandHelpers.ExecuteDomainAction(() => chain.Transfer(
            request.StepNo,
            request.FromActorType,
            request.FromActorRef,
            request.ToActorType,
            request.ToActorRef,
            request.RequestedByActorType,
            request.RequestedByActorRef,
            request.Reason,
            clock.UtcNow));
    }
}

file static class ApprovalActionCommandHelpers
{
    public static async Task<ApprovalChain> LoadScopedChainAsync(
        ApplicationDbContext dbContext,
        ApprovalChainId chainId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == chainId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId, cancellationToken)
            ?? throw new KnownException("审批链不存在或已不在当前业务范围。");
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
