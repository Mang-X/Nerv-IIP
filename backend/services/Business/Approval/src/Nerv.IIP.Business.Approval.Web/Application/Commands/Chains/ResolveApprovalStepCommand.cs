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
        // 取值权威在领域层 ApprovalDecisions（approve/reject/return），且 ApprovalChain.ResolveStep
        // 会先 ToLowerInvariant 归一化——校验器此前只认字面小写，比领域更严，"Approve" 这类大小写差异
        // 会被拦成一条没有合法值线索的 400（#1311）。这里与领域对齐成大小写不敏感，
        // 并把合法值写进中文领域消息：前端分层透传只原样上屏中文短消息，英文原文会被兜底吞掉。
        RuleFor(x => x.Decision)
            .Must(x => x is not null && ApprovalDecisions.StepResolutions.Contains(x.Trim()))
            .WithMessage("审批裁决取值非法，只能是 approve（同意）、reject（驳回）或 return（退回）。");
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
        try
        {
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
        catch (InvalidOperationException exception)
        {
            // 领域拒绝（非审批人 / 链已终态 / 步骤越序…）全部是业务约束，必须落成 400 + 中文短消息，
            // 而不是未捕获异常兜底出的 500 英文生码（#1327）。
            throw new KnownException(exception.Message);
        }
    }
}
