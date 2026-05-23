using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

namespace Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;

public sealed record GetApprovalChainQuery(ApprovalChainId ChainId) : IQuery<ApprovalChainResponse>;

public sealed record ApprovalChainResponse(
    string ChainId,
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    int TemplateVersion,
    string Status,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    IReadOnlyCollection<ApprovalStepResponse> Steps,
    IReadOnlyCollection<ApprovalDecisionResponse> Decisions);

public sealed record ApprovalStepResponse(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    string Status,
    DateTimeOffset? DueAtUtc,
    string? ResolvedDecision);

public sealed record ApprovalDecisionResponse(
    string DecisionId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAtUtc);

public sealed class GetApprovalChainQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetApprovalChainQuery, ApprovalChainResponse>
{
    public async Task<ApprovalChainResponse> Handle(GetApprovalChainQuery request, CancellationToken cancellationToken)
    {
        var chain = await dbContext.ApprovalChains
            .AsNoTracking()
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == request.ChainId, cancellationToken)
            ?? throw new KnownException("Approval chain was not found.");
        return new ApprovalChainResponse(
            chain.Id.ToString(),
            chain.OrganizationId,
            chain.EnvironmentId,
            chain.TemplateCode,
            chain.TemplateVersion,
            chain.Status,
            chain.DocumentReference.SourceService,
            chain.DocumentReference.DocumentType,
            chain.DocumentReference.DocumentId,
            chain.DocumentReference.DocumentLineId,
            chain.Steps.OrderBy(x => x.StepNo).ThenBy(x => x.ApproverRef).Select(x => new ApprovalStepResponse(
                x.StepNo,
                x.StepName,
                x.ParallelGroupKey,
                x.ApproverType,
                x.ApproverRef,
                x.Status,
                x.DueAtUtc,
                x.ResolvedDecision)).ToArray(),
            chain.Decisions.OrderBy(x => x.DecidedAtUtc).Select(x => new ApprovalDecisionResponse(
                x.Id.ToString(),
                x.StepNo,
                x.ActorType,
                x.ActorRef,
                x.Decision,
                x.Comment,
                x.DecidedAtUtc)).ToArray());
    }
}
