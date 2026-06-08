using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;

public sealed record ListLabelTemplatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<LabelTemplateListResult>;

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
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
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
        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim().ToLowerInvariant();
        var query = dbContext.LabelTemplates
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.TemplateCode)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new LabelTemplateSummary(x.Id, x.TemplateCode, x.TemplateName, x.TemplateFileId, x.VariableSchemaJson, x.Status))
            .ToArrayAsync(cancellationToken);
        return new LabelTemplateListResult(items, total);
    }
}
