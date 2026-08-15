using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataLeaderDemoScaleSeedServiceTests
{
    [Fact]
    public async Task Scale_seed_creates_four_work_centers_and_twenty_four_schedulable_devices_once()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoScaleSeedService(db);

        await seed.SeedAsync("org-001", "env-dev", 1000);
        await seed.SeedAsync("org-001", "env-dev", 1000);

        var workCenters = await db.WorkCenters.Where(x => x.Code.StartsWith("WC-SCALE-")).ToArrayAsync();
        Assert.Equal(4, workCenters.Length);
        Assert.All(workCenters, workCenter =>
        {
            Assert.Equal("STANDARD", workCenter.DefaultCalendarCode);
            Assert.Equal("LINE-SCALE-01", workCenter.LineCode);
            Assert.False(workCenter.Disabled);
        });

        var devices = await db.DeviceAssets.Where(x => x.Code.StartsWith("DEV-SCALE-")).ToArrayAsync();
        Assert.Equal(24, devices.Length);
        Assert.Equal(4, devices.Select(x => x.WorkCenterCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(devices, device => Assert.Equal(6, devices.Count(x => x.WorkCenterCode == device.WorkCenterCode)));

        Assert.Equal(6, await db.Skus.CountAsync(x => x.Code.StartsWith("SKU-SCALE-0")));
        Assert.Single(await db.Skus.Where(x => x.Code == "SKU-SCALE-RM-001").ToArrayAsync());
        var scaleCustomers = await db.BusinessPartners.Where(x => x.Code.StartsWith("CUST-SCALE-")).ToArrayAsync();
        Assert.Equal(4, scaleCustomers.Length);
        // #1290：规模块客户同样带信用额度档案（CNY），保证任选客户转订单不 400。
        Assert.All(scaleCustomers, x =>
        {
            Assert.NotNull(x.CreditLimit);
            Assert.Equal("CNY", x.CreditCurrencyCode);
        });
        Assert.Single(await db.ProductionLines.Where(x => x.Code == "LINE-SCALE-01").ToArrayAsync());
    }

    [Fact]
    public async Task Scale_seed_is_disabled_when_the_configured_order_count_is_not_positive()
    {
        await using var db = CreateDbContext();

        await new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 0);

        Assert.Empty(await db.WorkCenters.ToArrayAsync());
        Assert.Empty(await db.DeviceAssets.ToArrayAsync());
        Assert.Empty(await db.Skus.ToArrayAsync());
    }

    [Fact]
    public async Task Scale_seed_never_touches_the_frozen_leader_demo_segment()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");

        await new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 50);

        Assert.Single(await db.WorkCenters.Where(x => x.Code == "WC-CNC-DEMO").ToArrayAsync());
        Assert.Single(await db.DeviceAssets.Where(x => x.Code == "DEV-CNC-DEMO").ToArrayAsync());
        Assert.Single(await db.Skus.Where(x => x.Code == "SKU-DEMO-001").ToArrayAsync());
        Assert.Single(await db.BusinessPartners.Where(x => x.Code == "CUST-DEMO-001").ToArrayAsync());
        Assert.Empty(await db.WorkCenters.Where(x => x.Code.StartsWith("WC-SCALE-") && x.Code == "WC-CNC-DEMO").ToArrayAsync());
    }

    [Fact]
    public async Task Scale_seed_rejects_an_incompatible_reserved_scale_fact_without_overwriting_it()
    {
        await using var db = CreateDbContext();
        db.WorkCenters.Add(WorkCenter.CreateResource(
            "org-001", "env-dev", "WC-SCALE-WELD", "租户自维护工作中心", 480, "work-center",
            "SITE-001", "LINE-OTHER", "STANDARD", "minute", true));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 100));

        Assert.Contains("WC-SCALE-WELD", exception.Message, StringComparison.Ordinal);
        Assert.Equal("租户自维护工作中心", (await db.WorkCenters.SingleAsync(x => x.Code == "WC-SCALE-WELD")).Name);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"master-data-leader-demo-scale-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new ScaleSeedTestMediator());
    }

    private sealed class ScaleSeedTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
