using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record ProductCategoryItem(
    string CategoryCode,
    string CategoryName,
    string? ParentCode,
    string Path,
    string? Description,
    bool Enabled,
    string SnapshotVersion);

public sealed record ProductCategoryListResponse(IReadOnlyCollection<ProductCategoryItem> Items, int Total);

public sealed record ListProductCategoriesQuery(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? ParentCode = null,
    int Skip = 0,
    int Take = 100) : IQuery<ProductCategoryListResponse>;

public sealed record GetProductCategoryQuery(
    string OrganizationId,
    string EnvironmentId,
    string CategoryCode) : IQuery<ProductCategoryItem>;

public sealed class ListProductCategoriesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListProductCategoriesQuery, ProductCategoryListResponse>
{
    public async Task<ProductCategoryListResponse> Handle(ListProductCategoriesQuery request, CancellationToken cancellationToken)
    {
        var keyword = NormalizeKeyword(request.Search);
        var query = dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => !request.Enabled.HasValue || x.Disabled != request.Enabled.Value)
            .Where(x => string.IsNullOrWhiteSpace(request.ParentCode) || x.ParentCode == request.ParentCode)
            .Where(x => keyword == null || x.CategoryCode.ToLower().Contains(keyword) || x.CategoryName.ToLower().Contains(keyword));
        var total = await query.CountAsync(cancellationToken);
        var categories = await LoadCategoriesAsync(dbContext, request.OrganizationId, request.EnvironmentId, cancellationToken);
        var items = await query
            .OrderBy(x => x.CategoryCode)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToListAsync(cancellationToken);

        return new ProductCategoryListResponse(items.Select(x => ToItem(x, categories)).ToArray(), total);
    }

    internal static async Task<IReadOnlyDictionary<string, ProductCategory>> LoadCategoriesAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToListAsync(cancellationToken);
        return categories.ToDictionary(x => x.CategoryCode, StringComparer.OrdinalIgnoreCase);
    }

    internal static ProductCategoryItem ToItem(ProductCategory category, IReadOnlyDictionary<string, ProductCategory> categories)
    {
        return new ProductCategoryItem(
            category.CategoryCode,
            category.CategoryName,
            category.ParentCode,
            BuildPath(category, categories),
            category.Description,
            !category.Disabled,
            category.UpdatedAtUtc.ToString("O"));
    }

    private static string BuildPath(ProductCategory category, IReadOnlyDictionary<string, ProductCategory> categories)
    {
        var path = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = category;
        while (seen.Add(current.CategoryCode))
        {
            path.Push(current.CategoryCode);
            if (string.IsNullOrWhiteSpace(current.ParentCode) || !categories.TryGetValue(current.ParentCode, out current!))
            {
                break;
            }
        }

        return string.Join('/', path);
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim().ToLowerInvariant();
    }
}

public sealed class GetProductCategoryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetProductCategoryQuery, ProductCategoryItem>
{
    public async Task<ProductCategoryItem> Handle(GetProductCategoryQuery request, CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategories.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.CategoryCode == request.CategoryCode,
            cancellationToken)
            ?? throw new KnownException($"Product category '{request.CategoryCode}' was not found.");
        var categories = await ListProductCategoriesQueryHandler.LoadCategoriesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        return ListProductCategoriesQueryHandler.ToItem(category, categories);
    }
}
