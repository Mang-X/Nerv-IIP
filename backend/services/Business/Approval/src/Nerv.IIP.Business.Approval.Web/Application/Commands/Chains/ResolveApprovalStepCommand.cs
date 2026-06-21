using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record ResolveApprovalStepCommand(
    ApprovalChainId ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment) : ICommand<ApprovalDecisionId>;

public sealed class ResolveApprovalStepCommandValidator : AbstractValidator<ResolveApprovalStepCommand>
{
    public ResolveApprovalStepCommandValidator()
    {
        RuleFor(x => x.ChainId).NotEmpty();
        RuleFor(x => x.StepNo).GreaterThan(0);
        RuleFor(x => x.ActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ActorRef).RequiredApprovalCode(150);
        RuleFor(x => x.Decision).Must(x => x is "approve" or "reject" or "return").WithMessage("Decision must be approve, reject or return.");
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public sealed class ResolveApprovalStepCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ResolveApprovalStepCommand, ApprovalDecisionId>
{
    public async Task<ApprovalDecisionId> Handle(ResolveApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var chain = await dbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == request.ChainId, cancellationToken)
            ?? throw new KnownException("Approval chain was not found.");
        var actorType = request.ActorType.Trim().ToLowerInvariant();
        var actorRef = request.ActorRef.Trim();
        var nowUtc = DateTimeOffset.UtcNow;
        var matchingDelegations = await dbContext.ApprovalDelegations
            .AsNoTracking()
            .Where(x => x.OrganizationId == chain.OrganizationId
                && x.EnvironmentId == chain.EnvironmentId
                && x.Status == ApprovalDelegationStatuses.Active
                && x.DelegateActorType == actorType
                && x.DelegateActorRef == actorRef
                && (x.DocumentType == null || x.DocumentType == chain.DocumentReference.DocumentType)
                && x.EffectiveFromUtc <= nowUtc
                && x.EffectiveToUtc >= nowUtc)
            .OrderBy(x => x.EffectiveToUtc)
            .ToListAsync(cancellationToken);
        var matchingDelegation = matchingDelegations.FirstOrDefault(x => chain.Steps.Any(step =>
            step.StepNo == request.StepNo
            && step.Status == ApprovalStepStatuses.Pending
            && step.MatchesApprover(x.DelegatorActorType, x.DelegatorActorRef)));
        var decision = chain.ResolveStep(
            request.StepNo,
            request.ActorType,
            request.ActorRef,
            request.Decision,
            request.Comment,
            matchingDelegation?.DelegatorActorType,
            matchingDelegation?.DelegatorActorRef);
        return decision.Id;
    }
}
