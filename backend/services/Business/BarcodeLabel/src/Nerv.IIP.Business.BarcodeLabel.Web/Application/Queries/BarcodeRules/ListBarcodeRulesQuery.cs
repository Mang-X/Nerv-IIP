using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.BarcodeRules;

public sealed record ListBarcodeRulesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? Keyword,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake) : IQuery<BarcodeRuleListResult>;

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
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Keyword).MaximumLength(100);
    }
}

public sealed class ListBarcodeRulesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListBarcodeRulesQuery, BarcodeRuleListResult>
{
    public async Task<BarcodeRuleListResult> Handle(ListBarcodeRulesQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var keyword = SearchTerm.From(request.Keyword).Value;
        var query = dbContext.BarcodeRules
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        if (keyword is not null)
        {
            query = query.Where(x => x.RuleCode.ToLower().Contains(keyword) || x.Prefix.ToLower().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.RuleCode)
            .Skip(page.Skip)
            .Take(page.Take)
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
