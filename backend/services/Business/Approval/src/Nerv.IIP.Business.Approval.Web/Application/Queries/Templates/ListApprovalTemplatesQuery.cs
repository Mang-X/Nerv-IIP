using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.Approval.Web.Application.Queries.Templates;

public sealed record ListApprovalTemplatesQuery(
    string? OrganizationId,
    string? EnvironmentId,
    string? DocumentType,
    bool? IsActive,
    int Skip,
    int Take) : IQuery<ApprovalTemplateListResponse>;

public sealed record ApprovalTemplateListResponse(
    IReadOnlyCollection<ApprovalTemplateResponse> Items,
    int Total);

public sealed record ApprovalTemplateResponse(
    string TemplateId,
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string DocumentType,
    int Version,
    bool IsActive,
    IReadOnlyCollection<ApprovalTemplateStepResponse> Steps);

public sealed record ApprovalTemplateStepResponse(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    int? DueInHours);

public sealed class ListApprovalTemplatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListApprovalTemplatesQuery, ApprovalTemplateListResponse>
{
    public async Task<ApprovalTemplateListResponse> Handle(ListApprovalTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ApprovalTemplates.AsNoTracking().Include(x => x.Steps).AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            query = query.Where(x => x.OrganizationId == request.OrganizationId);
        }

        if (!string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            query = query.Where(x => x.EnvironmentId == request.EnvironmentId);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(x => x.DocumentType == request.DocumentType);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var templates = await query
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.EnvironmentId)
            .ThenBy(x => x.TemplateCode)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);
        var items = templates.Select(x => new ApprovalTemplateResponse(
            x.Id.ToString(),
            x.OrganizationId,
            x.EnvironmentId,
            x.TemplateCode,
            x.DocumentType,
            x.Version,
            x.IsActive,
            x.Steps
                .OrderBy(step => step.StepNo)
                .ThenBy(step => step.ApproverType)
                .ThenBy(step => step.ApproverRef)
                .Select(step => new ApprovalTemplateStepResponse(
                    step.StepNo,
                    step.StepName,
                    step.ParallelGroupKey,
                    step.ApproverType,
                    step.ApproverRef,
                    step.DueInHours))
                .ToArray()))
            .ToArray();
        return new ApprovalTemplateListResponse(items, total);
    }
}
