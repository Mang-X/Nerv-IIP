using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

/// <summary>
/// Inventory 产品基线 seed（#3770）：为全新环境补齐主线库位 <c>loc-raw-01 / loc-semi-01 / loc-fg-01 / loc-line-01</c>。
/// 库位码与 AppHost 在普通 Development 下发给 MES/WMS 的库位一致，也是 MasterData
/// <c>inventory-location</c> 码表的候选码；线边库位必须是 <c>line-side</c>，否则线边库存读面查不到它。
/// 本 seed 不依赖 LeaderDemo/WorldHistory；按 org/env + locationCode 幂等只补缺，
/// 已存在的库位（包括被租户改写类型或停用的）一律保留。
/// </summary>
public sealed class InventoryLocationSeedService(ApplicationDbContext dbContext)
{
    public const string SiteCode = "SITE-001";
    private const string ActiveStatus = "active";

    private sealed record LocationSeed(string LocationCode, string LocationType);

    private static readonly LocationSeed[] Locations =
    [
        new("loc-raw-01", "storage"),
        new("loc-semi-01", "storage"),
        new("loc-fg-01", "storage"),
        new("loc-line-01", "line-side"),
    ];

    public async Task<int> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var codes = Locations.Select(x => x.LocationCode).ToArray();
        var existing = (await dbContext.StockLocations
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                    && codes.Contains(x.LocationCode))
                .Select(x => x.LocationCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var seed in Locations.Where(x => !existing.Contains(x.LocationCode)))
        {
            dbContext.StockLocations.Add(StockLocation.CreateOrUpdate(
                existing: null,
                organizationId,
                environmentId,
                seed.LocationCode,
                seed.LocationType,
                SiteCode,
                parentLocationCode: null,
                ActiveStatus));
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return written;
    }
}
