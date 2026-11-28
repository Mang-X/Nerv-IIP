using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record ResolveApprovalStepCommand(
    string OrganizationId,
    string EnvironmentId,
    ApprovalChainId ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment,
    string? RoleRefs = null,
    string? DepartmentRef = null,
    ApprovalStepId? TargetStepId = null) : ICommand<ApprovalDecisionId?>;

public sealed class ResolveApprovalStepCommandValidator : AbstractValidator<ResolveApprovalStepCommand>
{
    public ResolveApprovalStepCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
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
    : ICommandHandler<ResolveApprovalStepCommand, ApprovalDecisionId?>
{
    public async Task<ApprovalDecisionId?> Handle(ResolveApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var chain = await dbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == request.ChainId
                && x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId, cancellationToken);
        if (chain is null)
        {
            return null;
        }

        var actorType = request.ActorType.Trim().ToLowerInvariant();
        var actorRef = request.ActorRef.Trim();
        var actors = await ApprovalTaskActors.LoadAsync(dbContext, request.OrganizationId, request.EnvironmentId,
            actorType, actorRef, request.RoleRefs, request.DepartmentRef, cancellationToken);
        var eligibleSteps = chain.Steps.Where(x => x.StepNo == request.StepNo
            && actors.Matches(x.ApproverType, x.ApproverRef, chain.DocumentReference.DocumentType));
        var step = request.TargetStepId is null
            ? eligibleSteps
                .Where(x => x.Status == ApprovalStepStatuses.Pending)
                .OrderByDescending(x => x.MatchesApprover(actorType, actorRef))
                .ThenByDescending(x => actors.IsDirect(x.ApproverType, x.ApproverRef))
                .ThenBy(x => x.ApproverType, StringComparer.Ordinal)
                .ThenBy(x => x.ApproverRef, StringComparer.Ordinal)
                .FirstOrDefault()
            : eligibleSteps.SingleOrDefault(x => x.Id == request.TargetStepId)
                ?? throw new KnownException("当前账号不能处理此审批任务。");
        var onBehalfOf = step is not null && !step.MatchesApprover(actorType, actorRef) ? step : null;
        try
        {
            var decision = chain.ResolveStep(
                request.StepNo,
                request.ActorType,
                request.ActorRef,
                request.Decision,
                request.Comment,
                onBehalfOf?.ApproverType,
                onBehalfOf?.ApproverRef);
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
