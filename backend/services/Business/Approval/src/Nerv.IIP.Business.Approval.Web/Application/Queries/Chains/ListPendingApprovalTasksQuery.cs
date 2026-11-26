using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;

public sealed record ListPendingApprovalTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string ActorType,
    string ActorRef,
    int Skip,
    int Take,
    string? RoleRefs = null,
    string? DepartmentRef = null) : IQuery<PendingApprovalTaskListResponse>;

public sealed record PendingApprovalTaskListResponse(
    IReadOnlyCollection<PendingApprovalTaskResponse> Items,
    int Total);

public sealed record PendingApprovalTaskResponse(
    string ChainId,
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    DateTimeOffset? DueAtUtc,
    string StepId);

public sealed class ListPendingApprovalTasksQueryValidator : AbstractValidator<ListPendingApprovalTasksQuery>
{
    public ListPendingApprovalTasksQueryValidator()
    {
        this.AddRequiredTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.ActorType).RequiredApprovalCode(50);
        RuleFor(x => x.ActorRef).RequiredApprovalCode(150);
        this.AddOffsetPageRules(x => x.Skip, x => x.Take);
    }
}

public sealed class ListPendingApprovalTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListPendingApprovalTasksQuery, PendingApprovalTaskListResponse>
{
    public async Task<PendingApprovalTaskListResponse> Handle(ListPendingApprovalTasksQuery request, CancellationToken cancellationToken)
    {
        var actorType = request.ActorType.Trim().ToLowerInvariant();
        var actorRef = request.ActorRef.Trim();
        var actors = await ApprovalTaskActors.LoadAsync(dbContext, request.OrganizationId, request.EnvironmentId,
            actorType, actorRef, request.RoleRefs, request.DepartmentRef, cancellationToken);
        var candidateRefs = actors.CandidateRefs.ToArray();
        var chains = await dbContext.ApprovalChains
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Status == ApprovalChainStatuses.Pending
                && x.Steps.Any(step =>
                    step.Status == ApprovalStepStatuses.Pending
                    && candidateRefs.Contains(step.ApproverRef)))
            .ToListAsync(cancellationToken);
        var items = chains
            .SelectMany(chain => chain.Steps
                .Where(step => step.Status == ApprovalStepStatuses.Pending
                    && actors.Matches(step.ApproverType, step.ApproverRef, chain.DocumentReference.DocumentType)
                    && chain.Steps.Where(previous => previous.StepNo < step.StepNo).GroupBy(previous => previous.StepNo).All(ApprovalStep.IsGroupComplete))
                .OrderByDescending(step => step.MatchesApprover(actorType, actorRef))
                .ThenByDescending(step => actors.IsDirect(step.ApproverType, step.ApproverRef))
                .ThenBy(step => step.ApproverType, StringComparer.Ordinal)
                .ThenBy(step => step.ApproverRef, StringComparer.Ordinal)
                .DistinctBy(step => step.StepNo)
                .Select(step => new PendingApprovalTaskResponse(
                    chain.Id.ToString(),
                    step.StepNo,
                    step.StepName,
                    step.ParallelGroupKey,
                    chain.DocumentReference.SourceService,
                    chain.DocumentReference.DocumentType,
                    chain.DocumentReference.DocumentId,
                    chain.DocumentReference.DocumentLineId,
                    step.DueAtUtc,
                    step.Id.ToString())))
            .OrderBy(x => x.DueAtUtc)
            .ThenBy(x => x.ChainId)
            .ThenBy(x => x.StepNo)
            .ToArray();
        return new PendingApprovalTaskListResponse(
            items.Skip(request.Skip).Take(request.Take).ToArray(),
            items.Length);
    }

}
