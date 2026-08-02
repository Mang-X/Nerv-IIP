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

    /// <summary>
    /// 规模池只是排产纵深的填充料，不是演示故事的一部分。
    ///
    /// <para>
    /// 销售订单读面按 <c>CreatedAtUtc</c> 倒序，而规模单原来不回填创建时间、直接拿「现在」，
    /// 于是**首屏十行全是 SO-SCALE-***——领导一打开销售订单页，看到的就是一批本不该点开的
    /// 填充单，且它们没有交期、紧急度却全判高风险（第五轮走查 owner 亲验点名）。
    /// </para>
    /// <para>
    /// 这条锁住「创建时间被压到很早」，而不是锁具体天数：换个偏移量仍应通过，
    /// 但「忘了回填」必红。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Scale_orders_are_backdated_so_they_never_head_the_newest_first_list()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await new LeaderDemoScaleSeedService(dbContext).SeedAsync("org-001", "env-dev", 50, NowUtc);

        var created = await dbContext.SalesOrders
            .Where(x => x.SalesOrderNo.StartsWith("SO-SCALE-"))
            .Select(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.NotEmpty(created);
        // 全部早于「现在」至少一年——世界观历史最早的订单也在一年内，规模单必须沉到它们之后。
        var cutoff = NowUtc.AddDays(-365).UtcDateTime;
        Assert.All(created, at => Assert.True(
            at < cutoff,
            $"规模单创建时间 {at:o} 不早于 {cutoff:o}：忘了回填，销售订单首屏会被填充单占满。"));

        // 按序号错开分钟：整批同一时刻会让倒序分页在边界抖动（同值排序不稳定）。
        Assert.True(
            created.Distinct().Count() > 1,
            "规模单创建时间完全相同，倒序分页会在边界上重复或漏行。");
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
