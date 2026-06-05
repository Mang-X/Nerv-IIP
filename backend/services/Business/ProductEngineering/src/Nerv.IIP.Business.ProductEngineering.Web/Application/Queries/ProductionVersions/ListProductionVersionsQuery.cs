using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Contracts.ProductEngineering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.ProductionVersions;

public sealed record ListProductionVersionsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListProductionVersionsResponse>;

public sealed class ListProductionVersionsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListProductionVersionsQuery, ListProductionVersionsResponse>
{
    public async Task<ListProductionVersionsResponse> Handle(ListProductionVersionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ProductionVersions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status);
        }

        var total = await query.CountAsync(cancellationToken);
        var versions = await query
            .OrderBy(x => x.SkuCode)
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.ValidFrom)
            .Skip(request.Skip)
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new
            {
                Id = x.Id.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.SkuCode,
                x.MbomVersionId,
                x.RoutingVersionId,
                x.ValidFrom,
                x.ValidTo,
                x.LotSizeMin,
                x.LotSizeMax,
                x.Priority,
                x.IsDefault,
                x.Status,
            })
            .ToArrayAsync(cancellationToken);

        var items = versions
            .Select(x => new ProductionVersionListItem(
                x.Id.ToString("D"),
                x.OrganizationId,
                x.EnvironmentId,
                x.SkuCode,
                x.MbomVersionId,
                x.RoutingVersionId,
                x.ValidFrom,
                x.ValidTo,
                x.LotSizeMin,
                x.LotSizeMax,
                x.Priority,
                x.IsDefault,
                x.Status))
            .ToArray();

        return new ListProductionVersionsResponse(items, total);
    }
}
