using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Queries.Delegations;

public sealed record ListApprovalDelegationsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? DelegatorActorRef,
    string? DelegateActorRef,
    string? DocumentType,
    int Skip,
    int Take) : IQuery<ApprovalDelegationListResponse>;

public sealed record ApprovalDelegationListResponse(
    IReadOnlyCollection<ApprovalDelegationListItem> Items,
    int Total);

public sealed record ApprovalDelegationListItem(
    string DelegationId,
    string OrganizationId,
    string EnvironmentId,
    string DelegatorActorType,
    string DelegatorActorRef,
    string DelegateActorType,
    string DelegateActorRef,
    string? DocumentType,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset EffectiveToUtc,
    string Status,
    string? Reason,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? RevokedBy,
    DateTimeOffset? RevokedAtUtc);

public sealed class ListApprovalDelegationsQueryValidator : AbstractValidator<ListApprovalDelegationsQuery>
{
    public ListApprovalDelegationsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.DelegatorActorRef).MaximumLength(150);
        RuleFor(x => x.DelegateActorRef).MaximumLength(150);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListApprovalDelegationsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListApprovalDelegationsQuery, ApprovalDelegationListResponse>
{
    public async Task<ApprovalDelegationListResponse> Handle(ListApprovalDelegationsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ApprovalDelegations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.DelegatorActorRef))
        {
            query = query.Where(x => x.DelegatorActorRef == request.DelegatorActorRef);
        }

        if (!string.IsNullOrWhiteSpace(request.DelegateActorRef))
        {
            query = query.Where(x => x.DelegateActorRef == request.DelegateActorRef);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(x => x.DocumentType == request.DocumentType);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new ApprovalDelegationListItem(
                x.Id.ToString(),
                x.OrganizationId,
                x.EnvironmentId,
                x.DelegatorActorType,
                x.DelegatorActorRef,
                x.DelegateActorType,
                x.DelegateActorRef,
                x.DocumentType,
                x.EffectiveFromUtc,
                x.EffectiveToUtc,
                x.Status,
                x.Reason,
                x.CreatedBy,
                x.CreatedAtUtc,
                x.RevokedBy,
                x.RevokedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ApprovalDelegationListResponse(items, total);
    }
}
