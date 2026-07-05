using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

public sealed record InspectionTaskResponse(
    Domain.AggregatesModel.InspectionTaskAggregate.InspectionTaskId InspectionTaskId,
    Domain.AggregatesModel.InspectionPlanAggregate.InspectionPlanId InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string SkuCode,
    decimal Quantity,
    string UomCode,
    string? BatchNo,
    string? SerialNo,
    string Status,
    DateTimeOffset DueAtUtc,
    DateTimeOffset CreatedAtUtc,
    Domain.AggregatesModel.InspectionRecordAggregate.InspectionRecordId? InspectionRecordId);

public sealed record ListInspectionTasksResponse(IReadOnlyCollection<InspectionTaskResponse> Items, int Total);

public sealed record ListInspectionTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? SkuCode,
    int Skip = 0,
    int Take = 100) : IQuery<ListInspectionTasksResponse>;

public sealed class ListInspectionTasksQueryValidator : AbstractValidator<ListInspectionTasksQuery>
{
    public ListInspectionTasksQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

public sealed class ListInspectionTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInspectionTasksQuery, ListInspectionTasksResponse>
{
    public async Task<ListInspectionTasksResponse> Handle(ListInspectionTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InspectionTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            var skuCode = request.SkuCode.Trim();
            query = query.Where(x => x.SkuCode == skuCode);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Status == "completed")
            .ThenBy(x => x.DueAtUtc)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new InspectionTaskResponse(
                x.Id,
                x.InspectionPlanId,
                x.SourceType,
                x.SourceService,
                x.SourceDocumentId,
                x.SourceDocumentLineId,
                x.SkuCode,
                x.Quantity,
                x.UomCode,
                x.BatchNo,
                x.SerialNo,
                x.Status,
                x.DueAtUtc,
                x.CreatedAtUtc,
                x.InspectionRecordId))
            .ToArrayAsync(cancellationToken);

        return new ListInspectionTasksResponse(items, total);
    }
}
