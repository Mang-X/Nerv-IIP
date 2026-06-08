using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;

public sealed record ListApprovalChainsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? StartedBy,
    string? SourceService,
    string? DocumentType,
    string? DocumentId,
    int Skip,
    int Take) : IQuery<ApprovalChainListResponse>;

public sealed record ApprovalChainListResponse(
    IReadOnlyCollection<ApprovalChainListItem> Items,
    int Total);

public sealed record ApprovalChainListItem(
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
    string StartedBy,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed class ListApprovalChainsQueryValidator : AbstractValidator<ListApprovalChainsQuery>
{
    public ListApprovalChainsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.StartedBy).MaximumLength(150);
        RuleFor(x => x.SourceService).MaximumLength(100);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.DocumentId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListApprovalChainsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListApprovalChainsQuery, ApprovalChainListResponse>
{
    public async Task<ApprovalChainListResponse> Handle(ListApprovalChainsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ApprovalChains
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.StartedBy))
        {
            query = query.Where(x => x.StartedBy == request.StartedBy);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceService))
        {
            query = query.Where(x => x.DocumentReference.SourceService == request.SourceService);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(x => x.DocumentReference.DocumentType == request.DocumentType);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentId))
        {
            query = query.Where(x => x.DocumentReference.DocumentId == request.DocumentId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new ApprovalChainListItem(
                x.Id.ToString(),
                x.OrganizationId,
                x.EnvironmentId,
                x.TemplateCode,
                x.TemplateVersion,
                x.Status,
                x.DocumentReference.SourceService,
                x.DocumentReference.DocumentType,
                x.DocumentReference.DocumentId,
                x.DocumentReference.DocumentLineId,
                x.StartedBy,
                x.StartedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ApprovalChainListResponse(items, total);
    }
}
