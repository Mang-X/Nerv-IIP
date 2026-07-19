using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class SalesOrderDemandDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_released_real_site_sales_order_and_is_idempotent()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new SalesOrderDemandDemoSeedService(dbContext);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var order = await dbContext.SalesOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(SalesOrderDemandDemoSeedService.SalesOrderNo, order.SalesOrderNo);
        Assert.Equal(SalesOrderDemandDemoSeedService.SiteCode, order.SiteCode);
        Assert.Equal("released", order.Status);
        Assert.Equal(1, order.Version);
        Assert.Equal(SalesOrderDemandDemoSeedService.SkuCode, Assert.Single(order.Lines).SkuCode);
        Assert.Single(await dbContext.Quotations.ToArrayAsync());
    }
}
