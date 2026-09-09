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
    IReadOnlyCollection<InspectionPlanCharacteristicResponse> Characteristics,
    decimal? TimeIntervalHours = null,
    decimal? QuantityInterval = null,
    string? AssignedInspectorUserId = null,
    string? AssignedTeamId = null);

public sealed record InspectionPlanCharacteristicResponse(
    string CharacteristicCode,
    string Name,
    string Method,
    string Severity,
    bool Required,
    string SamplingRule,
    string CharacteristicType,
    decimal? NominalValue,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    string? UnitCode,
    InspectionSamplingPlanResponse? SamplingPlan);

public sealed record InspectionSamplingPlanResponse(
    string InspectionLevel,
    string Aql,
    int SampleSize,
    int AcceptanceNumber,
    int RejectionNumber);

public sealed record ListInspectionPlansResponse(IReadOnlyCollection<InspectionPlanResponse> Items, int Total);

public sealed record ListInspectionPlansQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? Status,
    string? Keyword = null,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake) : IQuery<ListInspectionPlansResponse>;

public sealed class ListInspectionPlansQueryValidator : AbstractValidator<ListInspectionPlansQuery>
{
    public ListInspectionPlansQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        this.AddSearchTermRule(x => x.Keyword);
        this.AddOffsetPageRules(x => x.Skip, x => x.Take);
    }
}

public sealed class ListInspectionPlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInspectionPlansQuery, ListInspectionPlansResponse>
{
    public async Task<ListInspectionPlansResponse> Handle(ListInspectionPlansQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var search = SearchTerm.From(request.Keyword);
        var query = dbContext.InspectionPlans
            .AsNoTracking()
            .Include(x => x.Characteristics)
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId);

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

        if (search.Value is { } keyword)
        {
            var hasKeywordId = Guid.TryParse(keyword, out var keywordGuid);
            var keywordId = hasKeywordId ? new InspectionPlanId(keywordGuid) : null;
            query = query.Where(x =>
                (keywordId != null && x.Id == keywordId)
                || x.PlanCode.ToLower().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.Take)
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
                    c.SamplingRule,
                    c.CharacteristicType,
                    c.NominalValue,
                    c.LowerSpecLimit,
                    c.UpperSpecLimit,
                    c.UnitCode,
                    c.SamplingPlan == null
                        ? null
                        : new InspectionSamplingPlanResponse(
                            c.SamplingPlan.InspectionLevel,
                            c.SamplingPlan.Aql,
                            c.SamplingPlan.SampleSize,
                            c.SamplingPlan.AcceptanceNumber,
                            c.SamplingPlan.RejectionNumber))).ToArray(),
                x.TimeIntervalHours,
                x.QuantityInterval,
                x.AssignedInspectorUserId,
                x.AssignedTeamId))
            .ToListAsync(cancellationToken);

        return new ListInspectionPlansResponse(items, total);
    }
}
