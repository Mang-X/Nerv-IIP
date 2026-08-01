using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

public static class InventoryDirectoryTypes
{
    public const string Location = "location";
    public const string Batch = "batch";
    public const string Serial = "serial";
}

public static class InventoryDirectoryStableIds
{
    public static string Create(string directoryType, string skuCode, string code) =>
        string.Join(
            ':',
            "inventory-directory",
            directoryType,
            Encoding.UTF8.GetByteCount(skuCode),
            skuCode,
            Encoding.UTF8.GetByteCount(code),
            code);
}

public sealed class InventoryDirectoryValueRow
{
    public required string SkuCode { get; init; }

    public required string Code { get; init; }

    public DateTime SnapshotVersion { get; init; }
}

public static class InventoryDirectoryEfQueries
{
    public static IQueryable<InventoryDirectoryValueRow> BuildValues(
        ApplicationDbContext dbContext,
        ListInventoryDirectoryQuery request,
        string directoryType)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        var ledgers = dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.OnHandQuantity > 0)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => string.IsNullOrWhiteSpace(request.SkuCode) || x.SkuCode == request.SkuCode);

        return directoryType == InventoryDirectoryTypes.Batch
            ? ledgers
                .Where(x => x.LotNo != null)
                .Where(x => keyword == null
                    || x.LotNo!.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                    || x.SkuCode.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                    || x.LocationCode.ToLower().Contains(keyword))
                .GroupBy(x => new { x.SkuCode, x.LotNo })
                .Select(group => new InventoryDirectoryValueRow
                {
                    SkuCode = group.Key.SkuCode,
                    Code = group.Key.LotNo!,
                    SnapshotVersion = group.Max(x => x.UpdatedAtUtc),
                })
            : ledgers
                .Where(x => x.SerialNo != null)
                .Where(x => keyword == null
                    || x.SerialNo!.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                    || x.SkuCode.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                    || x.LocationCode.ToLower().Contains(keyword))
                .GroupBy(x => new { x.SkuCode, x.SerialNo })
                .Select(group => new InventoryDirectoryValueRow
                {
                    SkuCode = group.Key.SkuCode,
                    Code = group.Key.SerialNo!,
                    SnapshotVersion = group.Max(x => x.UpdatedAtUtc),
                });
    }

    public static IQueryable<int> BuildCount(IQueryable<InventoryDirectoryValueRow> values) =>
        values.GroupBy(_ => 1).Select(group => group.Count());

    public static IQueryable<InventoryDirectoryValueRow> BuildPage(
        IQueryable<InventoryDirectoryValueRow> values,
        int skip,
        int take) =>
        values
            .OrderBy(x => x.Code)
            .ThenBy(x => x.SkuCode)
            .Skip(skip)
            .Take(take);

    private static string? NormalizeKeyword(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public sealed record InventoryDirectoryItem(
    string Id,
    string Code,
    string Display,
    string DirectoryType,
    string? SiteCode,
    string? LocationCode,
    string? SkuCode,
    string? ParentCode,
    string SnapshotVersion);

public sealed record InventoryDirectoryResponse(
    IReadOnlyCollection<InventoryDirectoryItem> Items,
    int Total,
    int Skip,
    int Take,
    string Status,
    string SourceKind,
    DateTimeOffset AsOfUtc,
    string? ReasonCode = null);

public sealed record ListInventoryDirectoryQuery(
    string OrganizationId,
    string EnvironmentId,
    string DirectoryType,
    string? SiteCode = null,
    string? SkuCode = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 50) : IQuery<InventoryDirectoryResponse>;

public sealed class ListInventoryDirectoryQueryValidator : AbstractValidator<ListInventoryDirectoryQuery>
{
    public ListInventoryDirectoryQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DirectoryType).NotEmpty().MaximumLength(30);
        RuleFor(x => x.SiteCode).MaximumLength(100);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

public sealed class ListInventoryDirectoryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInventoryDirectoryQuery, InventoryDirectoryResponse>
{
    public async Task<InventoryDirectoryResponse> Handle(
        ListInventoryDirectoryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Skip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Skip, "Skip must be non-negative.");
        }

        if (request.Take is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Take, "Take must be between 1 and 200.");
        }

        var directoryType = request.DirectoryType.Trim().ToLowerInvariant();
        var skip = request.Skip;
        var take = request.Take;

        return directoryType switch
        {
            InventoryDirectoryTypes.Location => await ListLocationsAsync(request, skip, take, cancellationToken),
            InventoryDirectoryTypes.Batch => await ListLedgerValuesAsync(request, directoryType, skip, take, cancellationToken),
            InventoryDirectoryTypes.Serial => await ListLedgerValuesAsync(request, directoryType, skip, take, cancellationToken),
            _ => Unavailable(skip, take),
        };
    }

    private async Task<InventoryDirectoryResponse> ListLocationsAsync(
        ListInventoryDirectoryQuery request,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        var query = dbContext.StockLocations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.Status == "active")
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => keyword == null
                || x.LocationCode.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || x.LocationType.ToLower().Contains(keyword)
                || x.SiteCode.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || x.ParentLocationCode != null && x.ParentLocationCode.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            var skuCode = request.SkuCode.Trim();
            query = query.Where(location => dbContext.StockLedgers.Any(ledger =>
                ledger.OrganizationId == request.OrganizationId
                && ledger.EnvironmentId == request.EnvironmentId
                && ledger.SiteCode == location.SiteCode
                && ledger.LocationCode == location.LocationCode
                && ledger.SkuCode == skuCode
                && ledger.OnHandQuantity > 0));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.LocationCode)
            .Skip(skip)
            .Take(take)
            .Select(x => new InventoryDirectoryItem(
                x.Id.ToString(),
                x.LocationCode,
                x.LocationCode,
                InventoryDirectoryTypes.Location,
                x.SiteCode,
                x.LocationCode,
                null,
                x.ParentLocationCode,
                x.UpdatedAtUtc.ToString("O")))
            .ToArrayAsync(cancellationToken);

        return Available(items, total, skip, take, "inventory.stock-locations");
    }

    private async Task<InventoryDirectoryResponse> ListLedgerValuesAsync(
        ListInventoryDirectoryQuery request,
        string directoryType,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var values = InventoryDirectoryEfQueries.BuildValues(dbContext, request, directoryType);
        var total = await InventoryDirectoryEfQueries.BuildCount(values)
            .SingleOrDefaultAsync(cancellationToken);
        var page = await InventoryDirectoryEfQueries.BuildPage(values, skip, take)
            .ToArrayAsync(cancellationToken);
        var items = page
            .Select(x => new InventoryDirectoryItem(
                InventoryDirectoryStableIds.Create(directoryType, x.SkuCode, x.Code),
                x.Code,
                x.Code + " · " + x.SkuCode,
                directoryType,
                request.SiteCode,
                null,
                x.SkuCode,
                null,
                x.SnapshotVersion.ToString("O")))
            .ToArray();
        return Available(items, total, skip, take, "inventory.stock-ledgers");
    }

    private static InventoryDirectoryResponse Available(
        IReadOnlyCollection<InventoryDirectoryItem> items,
        int total,
        int skip,
        int take,
        string sourceKind) =>
        new(items, total, skip, take, "available", sourceKind, DateTimeOffset.UtcNow);

    private static InventoryDirectoryResponse Unavailable(int skip, int take) =>
        new([], 0, skip, take, "unsupported", "inventory", DateTimeOffset.UtcNow, "inventory-directory-type-unsupported");

    private static string? NormalizeKeyword(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
