using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;

public sealed record ListApprovalDecisionsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ChainId,
    string? ActorType,
    string? ActorRef,
    string? Decision,
    string? DocumentType,
    string? DocumentId,
    int Skip,
    int Take) : IQuery<ApprovalDecisionListResponse>;

public sealed record ApprovalDecisionListResponse(
    IReadOnlyCollection<ApprovalDecisionListItem> Items,
    int Total);

public sealed record ApprovalDecisionListItem(
    string DecisionId,
    string ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAtUtc,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId);

public sealed class ListApprovalDecisionsQueryValidator : AbstractValidator<ListApprovalDecisionsQuery>
{
    public ListApprovalDecisionsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.ChainId).MaximumLength(150);
        RuleFor(x => x.ActorType).MaximumLength(50);
        RuleFor(x => x.ActorRef).MaximumLength(150);
        RuleFor(x => x.Decision).MaximumLength(50);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.DocumentId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListApprovalDecisionsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListApprovalDecisionsQuery, ApprovalDecisionListResponse>
{
    public async Task<ApprovalDecisionListResponse> Handle(ListApprovalDecisionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ApprovalChains
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.ChainId))
        {
            var chainId = new ApprovalChainId(Guid.Parse(request.ChainId));
            query = query.Where(x => x.Id == chainId);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(x => x.DocumentReference.DocumentType == request.DocumentType);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentId))
        {
            query = query.Where(x => x.DocumentReference.DocumentId == request.DocumentId);
        }

        var decisions = query.SelectMany(
            chain => chain.Decisions,
            (chain, decision) => new
            {
                Chain = chain,
                Decision = decision,
            });

        if (!string.IsNullOrWhiteSpace(request.ActorType))
        {
            decisions = decisions.Where(x => x.Decision.ActorType == request.ActorType);
        }

        if (!string.IsNullOrWhiteSpace(request.ActorRef))
        {
            decisions = decisions.Where(x => x.Decision.ActorRef == request.ActorRef);
        }

        if (!string.IsNullOrWhiteSpace(request.Decision))
        {
            decisions = decisions.Where(x => x.Decision.Decision == request.Decision);
        }

        var total = await decisions.CountAsync(cancellationToken);
        var items = await decisions
            .OrderByDescending(x => x.Decision.DecidedAtUtc)
            .ThenByDescending(x => x.Decision.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new ApprovalDecisionListItem(
                x.Decision.Id.ToString(),
                x.Chain.Id.ToString(),
                x.Decision.StepNo,
                x.Decision.ActorType,
                x.Decision.ActorRef,
                x.Decision.Decision,
                x.Decision.Comment,
                x.Decision.DecidedAtUtc,
                x.Chain.DocumentReference.SourceService,
                x.Chain.DocumentReference.DocumentType,
                x.Chain.DocumentReference.DocumentId,
                x.Chain.DocumentReference.DocumentLineId))
            .ToArrayAsync(cancellationToken);

        return new ApprovalDecisionListResponse(items, total);
    }
}
