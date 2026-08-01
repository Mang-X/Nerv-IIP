using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

public static class InventoryDirectoryTypes
{
    public const string Location = "location";
    public const string Batch = "batch";
    public const string Serial = "serial";
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
        var directoryType = request.DirectoryType.Trim().ToLowerInvariant();
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 200);

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
                || x.LocationCode.ToLower().Contains(keyword)
                || x.LocationType.ToLower().Contains(keyword)
                || x.SiteCode.ToLower().Contains(keyword)
                || x.ParentLocationCode != null && x.ParentLocationCode.ToLower().Contains(keyword));

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
        var keyword = NormalizeKeyword(request.Keyword);
        var query = dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.OnHandQuantity > 0)
            .Where(x => string.IsNullOrWhiteSpace(request.SiteCode) || x.SiteCode == request.SiteCode)
            .Where(x => string.IsNullOrWhiteSpace(request.SkuCode) || x.SkuCode == request.SkuCode);

        if (directoryType == InventoryDirectoryTypes.Batch)
        {
            var batches = query
                .Where(x => x.LotNo != null)
                .Where(x => keyword == null
                    || x.LotNo!.ToLower().Contains(keyword)
                    || x.SkuCode.ToLower().Contains(keyword)
                    || x.LocationCode.ToLower().Contains(keyword))
                .GroupBy(x => new { x.SkuCode, x.LotNo })
                .Select(group => new
                {
                    group.Key.SkuCode,
                    Code = group.Key.LotNo!,
                    SnapshotVersion = group.Max(x => x.UpdatedAtUtc),
                });
            var total = await batches.CountAsync(cancellationToken);
            var items = await batches
                .OrderBy(x => x.Code)
                .ThenBy(x => x.SkuCode)
                .Skip(skip)
                .Take(take)
                .Select(x => new InventoryDirectoryItem(
                    x.SkuCode + ":" + x.Code,
                    x.Code,
                    x.Code + " · " + x.SkuCode,
                    InventoryDirectoryTypes.Batch,
                    request.SiteCode,
                    null,
                    x.SkuCode,
                    null,
                    x.SnapshotVersion.ToString("O")))
                .ToArrayAsync(cancellationToken);
            return Available(items, total, skip, take, "inventory.stock-ledgers");
        }

        var serials = query
            .Where(x => x.SerialNo != null)
            .Where(x => keyword == null
                || x.SerialNo!.ToLower().Contains(keyword)
                || x.SkuCode.ToLower().Contains(keyword)
                || x.LocationCode.ToLower().Contains(keyword))
            .GroupBy(x => new { x.SkuCode, x.SerialNo })
            .Select(group => new
            {
                group.Key.SkuCode,
                Code = group.Key.SerialNo!,
                SnapshotVersion = group.Max(x => x.UpdatedAtUtc),
            });
        var serialTotal = await serials.CountAsync(cancellationToken);
        var serialItems = await serials
            .OrderBy(x => x.Code)
            .ThenBy(x => x.SkuCode)
            .Skip(skip)
            .Take(take)
            .Select(x => new InventoryDirectoryItem(
                x.SkuCode + ":" + x.Code,
                x.Code,
                x.Code + " · " + x.SkuCode,
                InventoryDirectoryTypes.Serial,
                request.SiteCode,
                null,
                x.SkuCode,
                null,
                x.SnapshotVersion.ToString("O")))
            .ToArrayAsync(cancellationToken);
        return Available(serialItems, serialTotal, skip, take, "inventory.stock-ledgers");
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
