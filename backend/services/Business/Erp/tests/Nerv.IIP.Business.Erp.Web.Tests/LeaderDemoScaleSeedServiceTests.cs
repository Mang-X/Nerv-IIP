using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class LeaderDemoScaleSeedServiceTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 26, 3, 14, 15, TimeSpan.Zero);

    [Fact]
    public async Task Scale_seed_creates_the_configured_released_sales_orders_once()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new LeaderDemoScaleSeedService(dbContext);

        await seed.SeedAsync("org-001", "env-dev", 250, NowUtc);
        await seed.SeedAsync("org-001", "env-dev", 250, NowUtc);

        Assert.Equal(250, await dbContext.SalesOrders.CountAsync(x => x.SalesOrderNo.StartsWith("SO-SCALE-")));
        Assert.Equal(250, await dbContext.Quotations.CountAsync(x => x.QuotationNo.StartsWith("QUO-SCALE-")));

        var first = await dbContext.SalesOrders
            .Include(x => x.Lines)
            .SingleAsync(x => x.SalesOrderNo == "SO-SCALE-00001");
        Assert.Equal("released", first.Status);
        Assert.Equal("SITE-001", first.SiteCode);
        Assert.Equal("CUST-SCALE-001", first.CustomerCode);
        var line = Assert.Single(first.Lines);
        Assert.Equal("SKU-SCALE-001", line.SkuCode);
        Assert.Equal(20m, line.OrderedQuantity);
        Assert.Equal(new DateOnly(2026, 8, 9), line.RequiredDate);

        // 只写销售侧前置事实：不产生发货或任何结果事实。
        Assert.All(
            await dbContext.SalesOrders.Include(x => x.Lines).Where(x => x.SalesOrderNo.StartsWith("SO-SCALE-")).ToArrayAsync(),
            order => Assert.All(order.Lines, orderLine => Assert.Equal(orderLine.OrderedQuantity, orderLine.OpenQuantity)));
    }

    [Fact]
    public async Task Scale_seed_is_disabled_when_the_configured_order_count_is_not_positive()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await new LeaderDemoScaleSeedService(dbContext).SeedAsync("org-001", "env-dev", 0, NowUtc);

        Assert.Empty(await dbContext.SalesOrders.ToArrayAsync());
        Assert.Empty(await dbContext.Quotations.ToArrayAsync());
    }

    [Theory]
    // 黄金向量：ERP 与 MES 必须对同一序号派生完全相同的 SKU / 数量 / 交期偏移 / 优先级。
    [InlineData(1, "SKU-SCALE-001", 20, 14, 2)]
    [InlineData(2, "SKU-SCALE-002", 30, 15, 3)]
    [InlineData(29, "SKU-SCALE-005", 50, 42, 100)]
    [InlineData(1000, "SKU-SCALE-004", 60, 27, 2)]
    public void Scale_order_distribution_stays_on_the_shared_golden_vector(
        int index,
        string skuCode,
        int quantity,
        int dueDayOffset,
        int priority)
    {
        Assert.Equal(skuCode, LeaderDemoScaleSpec.SkuCode(index));
        Assert.Equal(quantity, LeaderDemoScaleSpec.Quantity(index));
        Assert.Equal(dueDayOffset, LeaderDemoScaleSpec.DueDayOffset(index));
        Assert.Equal(priority, LeaderDemoScaleSpec.Priority(index));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseInMemoryDatabase($"erp-leader-demo-scale-seed-{Guid.CreateVersion7():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }
}
