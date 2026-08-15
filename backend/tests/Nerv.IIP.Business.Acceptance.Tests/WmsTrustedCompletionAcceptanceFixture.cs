using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

internal static class WmsTrustedCompletionAcceptanceFixture
{
    public const string ActorPrincipalId = "acceptance-warehouse-operator";
    public const string PoolCode = "POOL-ACCEPTANCE-SITE-01";

    public static async Task SeedAsync(
        WmsDbContext dbContext,
        string organizationId,
        string environmentId,
        string siteCode)
    {
        dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            organizationId,
            environmentId,
            PoolCode,
            "验收仓储作业池",
            siteCode));
        dbContext.WarehouseWorkPoolMemberships.Add(
            WarehouseWorkPoolMembership.Create(
                organizationId,
                environmentId,
                PoolCode,
                ActorPrincipalId,
                DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow.AddHours(1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
