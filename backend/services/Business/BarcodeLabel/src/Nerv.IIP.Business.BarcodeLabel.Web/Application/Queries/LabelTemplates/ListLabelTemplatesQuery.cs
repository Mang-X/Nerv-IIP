using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;

public sealed record ListLabelTemplatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake) : IQuery<LabelTemplateListResult>;

public sealed record LabelTemplateListResult(IReadOnlyCollection<LabelTemplateSummary> Items, int Total);

public sealed record LabelTemplateSummary(
    LabelTemplateId TemplateId,
    string TemplateCode,
    string TemplateName,
    string TemplateFileId,
    string VariableSchemaJson,
    string Status);

public sealed class ListLabelTemplatesQueryValidator : AbstractValidator<ListLabelTemplatesQuery>
{
    public ListLabelTemplatesQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListLabelTemplatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListLabelTemplatesQuery, LabelTemplateListResult>
{
    public async Task<LabelTemplateListResult> Handle(ListLabelTemplatesQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim().ToLowerInvariant();
        var query = dbContext.LabelTemplates
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId && x.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.TemplateCode)
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(x => new LabelTemplateSummary(x.Id, x.TemplateCode, x.TemplateName, x.TemplateFileId, x.VariableSchemaJson, x.Status))
            .ToArrayAsync(cancellationToken);
        return new LabelTemplateListResult(items, total);
    }
}
