using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;

public sealed record ListLabelTemplatesQuery(string OrganizationId, string EnvironmentId, string? Status) : IQuery<IReadOnlyCollection<LabelTemplateSummary>>;

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
    }
}

public sealed class ListLabelTemplatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListLabelTemplatesQuery, IReadOnlyCollection<LabelTemplateSummary>>
{
    public async Task<IReadOnlyCollection<LabelTemplateSummary>> Handle(ListLabelTemplatesQuery request, CancellationToken cancellationToken)
    {
        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim().ToLowerInvariant();
        return await dbContext.LabelTemplates
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Status == status)
            .OrderBy(x => x.TemplateCode)
            .Select(x => new LabelTemplateSummary(x.Id, x.TemplateCode, x.TemplateName, x.TemplateFileId, x.VariableSchemaJson, x.Status))
            .ToArrayAsync(cancellationToken);
    }
}
