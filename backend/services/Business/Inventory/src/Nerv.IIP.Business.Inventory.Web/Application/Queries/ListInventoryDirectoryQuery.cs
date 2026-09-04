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
        TenantScope tenant,
        SearchTerm searchTerm,
        string directoryType)
    {
        var keyword = searchTerm.Value;
        var ledgers = dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => x.OnHandQuantity > 0)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => string.IsNullOrWhiteSpace(request.SkuCode) || x.SkuCode == request.SkuCode);

        return directoryType == InventoryDirectoryTypes.Batch
            ? ledgers
                .Where(x => x.LotNo != null)
                .Where(x => keyword == null
                    || x.LotNo!.ToLower().Contains(keyword)
                    || x.SkuCode.ToLower().Contains(keyword)
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
                    || x.SerialNo!.ToLower().Contains(keyword)
                    || x.SkuCode.ToLower().Contains(keyword)
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
    int Take = OffsetPage.DefaultTake) : IQuery<InventoryDirectoryResponse>;

public sealed class ListInventoryDirectoryQueryValidator : AbstractValidator<ListInventoryDirectoryQuery>
{
    public ListInventoryDirectoryQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
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
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var searchTerm = SearchTerm.From(request.Keyword);
        var directoryType = request.DirectoryType.Trim().ToLowerInvariant();

        return directoryType switch
        {
            InventoryDirectoryTypes.Location => await ListLocationsAsync(request, tenant, page, searchTerm, cancellationToken),
            InventoryDirectoryTypes.Batch => await ListLedgerValuesAsync(request, tenant, page, searchTerm, directoryType, cancellationToken),
            InventoryDirectoryTypes.Serial => await ListLedgerValuesAsync(request, tenant, page, searchTerm, directoryType, cancellationToken),
            _ => Unavailable(page.Skip, page.Take),
        };
    }

    private async Task<InventoryDirectoryResponse> ListLocationsAsync(
        ListInventoryDirectoryQuery request,
        TenantScope tenant,
        OffsetPage page,
        SearchTerm searchTerm,
        CancellationToken cancellationToken)
    {
        var keyword = searchTerm.Value;
        var query = dbContext.StockLocations
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId)
            .Where(x => x.Status == "active")
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => keyword == null
                || x.LocationCode.ToLower().Contains(keyword)
                || x.LocationType.ToLower().Contains(keyword)
                || x.SiteCode.ToLower().Contains(keyword)
                || x.ParentLocationCode != null && x.ParentLocationCode.ToLower().Contains(keyword));
        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            var skuCode = request.SkuCode.Trim();
            query = query.Where(location => dbContext.StockLedgers.Any(ledger =>
                ledger.OrganizationId == tenant.OrganizationId
                && ledger.EnvironmentId == tenant.EnvironmentId
                && ledger.SiteCode == location.SiteCode
                && ledger.LocationCode == location.LocationCode
                && ledger.SkuCode == skuCode
                && ledger.OnHandQuantity > 0));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.LocationCode)
            .Skip(page.Skip)
            .Take(page.Take)
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

        return Available(items, total, page.Skip, page.Take, "inventory.stock-locations");
    }

    private async Task<InventoryDirectoryResponse> ListLedgerValuesAsync(
        ListInventoryDirectoryQuery request,
        TenantScope tenant,
        OffsetPage page,
        SearchTerm searchTerm,
        string directoryType,
        CancellationToken cancellationToken)
    {
        var values = InventoryDirectoryEfQueries.BuildValues(dbContext, request, tenant, searchTerm, directoryType);
        var total = await InventoryDirectoryEfQueries.BuildCount(values)
            .SingleOrDefaultAsync(cancellationToken);
        var rows = await InventoryDirectoryEfQueries.BuildPage(values, page.Skip, page.Take)
            .ToArrayAsync(cancellationToken);
        var items = rows
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
        return Available(items, total, page.Skip, page.Take, "inventory.stock-ledgers");
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
}
