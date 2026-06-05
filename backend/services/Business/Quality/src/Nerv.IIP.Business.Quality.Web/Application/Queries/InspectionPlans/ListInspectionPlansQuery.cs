using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionPlans;

public sealed record InspectionPlanResponse(
    InspectionPlanId InspectionPlanId,
    string OrganizationId,
    string EnvironmentId,
    string PlanCode,
    string Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? DocumentType,
    int Version,
    string Status,
    IReadOnlyCollection<InspectionPlanCharacteristicResponse> Characteristics);

public sealed record InspectionPlanCharacteristicResponse(
    string CharacteristicCode,
    string Name,
    string Method,
    string Severity,
    bool Required,
    string SamplingRule);

public sealed record ListInspectionPlansResponse(IReadOnlyCollection<InspectionPlanResponse> Items, int Total);

public sealed record ListInspectionPlansQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListInspectionPlansResponse>;

public sealed class ListInspectionPlansQueryValidator : AbstractValidator<ListInspectionPlansQuery>
{
    public ListInspectionPlansQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListInspectionPlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInspectionPlansQuery, ListInspectionPlansResponse>
{
    public async Task<ListInspectionPlansResponse> Handle(ListInspectionPlansQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InspectionPlans
            .AsNoTracking()
            .Include(x => x.Characteristics)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(x => x.Category == request.Category);
        }

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.PartnerId))
        {
            query = query.Where(x => x.PartnerId == request.PartnerId);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId))
        {
            query = query.Where(x => x.WorkCenterId == request.WorkCenterId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(request.Skip)
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new InspectionPlanResponse(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.PlanCode,
                x.Category,
                x.SkuCode,
                x.PartnerId,
                x.WorkCenterId,
                x.DeviceAssetId,
                x.DocumentType,
                x.Version,
                x.Status,
                x.Characteristics.Select(c => new InspectionPlanCharacteristicResponse(
                    c.CharacteristicCode,
                    c.Name,
                    c.Method,
                    c.Severity,
                    c.IsRequired,
                    c.SamplingRule)).ToArray()))
            .ToListAsync(cancellationToken);

        return new ListInspectionPlansResponse(items, total);
    }
}
