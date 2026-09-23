using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

/// <summary>
/// 库位主数据维护读面（#3770）。与可搜索目录的 location 分支不同：目录只给选择器列启用库位、
/// 不带库位类型与状态；维护页必须看到全部库位（含停用）以及类型、状态，才能编辑与重新启用。
/// </summary>
public sealed record ListStockLocationsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 50) : IQuery<StockLocationListResponse>;

public sealed record StockLocationListResponse(
    IReadOnlyCollection<StockLocationLineResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record StockLocationLineResponse(
    string LocationId,
    string LocationCode,
    string LocationType,
    string SiteCode,
    string? ParentLocationCode,
    string Status,
    DateTime UpdatedAtUtc);

public sealed class ListStockLocationsQueryValidator : AbstractValidator<ListStockLocationsQuery>
{
    public ListStockLocationsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.Keyword).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, StockCountQueryLimits.MaxPageSize);
    }
}

public sealed class ListStockLocationsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListStockLocationsQuery, StockLocationListResponse>
{
    public async Task<StockLocationListResponse> Handle(
        ListStockLocationsQuery request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.StockLocations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var keyword = request.Keyword?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(x => x.LocationCode.ToLower().Contains(keyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.LocationCode)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new StockLocationLineResponse(
                x.Id.ToString(),
                x.LocationCode,
                x.LocationType,
                x.SiteCode,
                x.ParentLocationCode,
                x.Status,
                x.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new StockLocationListResponse(items, totalCount, request.Page, request.PageSize);
    }
}
