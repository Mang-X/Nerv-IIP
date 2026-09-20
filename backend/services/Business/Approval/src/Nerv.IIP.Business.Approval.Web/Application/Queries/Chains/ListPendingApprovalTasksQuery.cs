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
    int Take) : IQuery<PendingApprovalTaskListResponse>;

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
    DateTimeOffset? DueAtUtc);

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
        var chains = await dbContext.ApprovalChains
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Status == ApprovalChainStatuses.Pending
                && x.Steps.Any(step =>
                    step.Status == ApprovalStepStatuses.Pending
                    && step.ApproverType == actorType
                    && step.ApproverRef == request.ActorRef))
            .ToListAsync(cancellationToken);
        var items = chains
            .SelectMany(chain => chain.Steps
                .Where(step => step.Status == ApprovalStepStatuses.Pending
                    && step.ApproverType == actorType
                    && step.ApproverRef == request.ActorRef
                    && chain.Steps.Where(previous => previous.StepNo < step.StepNo).GroupBy(previous => previous.StepNo).All(ApprovalStep.IsGroupComplete))
                .Select(step => new PendingApprovalTaskResponse(
                    chain.Id.ToString(),
                    step.StepNo,
                    step.StepName,
                    step.ParallelGroupKey,
                    chain.DocumentReference.SourceService,
                    chain.DocumentReference.DocumentType,
                    chain.DocumentReference.DocumentId,
                    chain.DocumentReference.DocumentLineId,
                    step.DueAtUtc)))
            .OrderBy(x => x.DueAtUtc)
            .ThenBy(x => x.ChainId)
            .ToArray();
        return new PendingApprovalTaskListResponse(
            items.Skip(request.Skip).Take(request.Take).ToArray(),
            items.Length);
    }

}
