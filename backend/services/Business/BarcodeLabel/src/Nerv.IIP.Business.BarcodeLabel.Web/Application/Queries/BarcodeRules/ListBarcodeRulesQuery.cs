using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.BarcodeRules;

public sealed record ListBarcodeRulesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? Keyword,
    int Skip = 0,
    int Take = 100) : IQuery<BarcodeRuleListResult>;

public sealed record BarcodeRuleListResult(IReadOnlyCollection<BarcodeRuleSummary> Items, int Total);

public sealed record BarcodeRuleSummary(
    BarcodeRuleId BarcodeRuleId,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    int? Gs1CompanyPrefixLength,
    IReadOnlyCollection<string> AllowedSourceDocumentTypes,
    string Status);

public sealed class ListBarcodeRulesQueryValidator : AbstractValidator<ListBarcodeRulesQuery>
{
    public ListBarcodeRulesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Keyword).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListBarcodeRulesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListBarcodeRulesQuery, BarcodeRuleListResult>
{
    public async Task<BarcodeRuleListResult> Handle(ListBarcodeRulesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.BarcodeRules
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.RuleCode.Contains(keyword) || x.Prefix.Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.RuleCode)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new BarcodeRuleSummary(
                x.Id,
                x.RuleCode,
                x.BarcodeType,
                x.Prefix,
                x.Length,
                x.ChecksumRule,
                x.Gs1CompanyPrefixLength,
                x.AllowedSourceDocumentTypes,
                x.Status))
            .ToArrayAsync(cancellationToken);
        return new BarcodeRuleListResult(items, total);
    }
}
