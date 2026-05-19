namespace Nerv.IIP.Iam.Web.Application;

public sealed record IamListQueryOptions(
    int PageIndex,
    int PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch,
    bool? FilterEnabled,
    bool? FilterRevoked)
{
    private const int DefaultPageIndex = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    public static IamListQueryOptions Create(
        int? pageIndex,
        int? pageSize,
        string? sortBy,
        string? sortOrder,
        string? filterSearch,
        bool? filterEnabled = null,
        bool? filterRevoked = null)
    {
        var normalizedPageIndex = pageIndex is > 0 ? pageIndex.Value : DefaultPageIndex;
        var normalizedPageSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;
        return new IamListQueryOptions(
            normalizedPageIndex,
            normalizedPageSize,
            Normalize(sortBy),
            Normalize(sortOrder),
            Normalize(filterSearch),
            filterEnabled,
            filterRevoked);
    }

    public bool IsDescending => string.Equals(SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

    public int Skip => (PageIndex - 1) * PageSize;

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record PagedListResponse<T>(
    int PageIndex,
    int PageSize,
    int TotalCount,
    IReadOnlyList<T> Items);

public static class IamListPaging
{
    public static PagedListResponse<T> ToPagedResponse<T>(
        this IEnumerable<T> source,
        IamListQueryOptions options)
    {
        var rows = source.ToArray();
        var items = rows
            .Skip(options.Skip)
            .Take(options.PageSize)
            .ToArray();

        return new PagedListResponse<T>(options.PageIndex, options.PageSize, rows.Length, items);
    }
}
