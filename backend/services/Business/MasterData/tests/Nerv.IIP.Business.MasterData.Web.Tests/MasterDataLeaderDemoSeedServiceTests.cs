using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataLeaderDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_each_reserved_demo_fact_once()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Single(await db.Sites.Where(x => x.Code == "SITE-001").ToArrayAsync());
        Assert.Single(await db.ProductionLines.Where(x => x.Code == "LINE-DEMO-01").ToArrayAsync());
        Assert.Single(await db.WorkCenters.Where(x => x.Code == "WC-CNC-DEMO").ToArrayAsync());
        Assert.Single(await db.Skus.Where(x => x.Code == "SKU-DEMO-001").ToArrayAsync());
        Assert.Single(await db.Skus.Where(x => x.Code == "SKU-DEMO-RM-001").ToArrayAsync());
        Assert.Single(await db.BusinessPartners.Where(x => x.Code == "CUST-DEMO-001").ToArrayAsync());
        var device = Assert.Single(await db.DeviceAssets.Where(x => x.Code == "DEV-CNC-DEMO").ToArrayAsync());
        Assert.Equal("LINE-DEMO-01", device.LineCode);
        Assert.Equal("WC-CNC-DEMO", device.WorkCenterCode);
        Assert.True(device.TelemetryEnabled);
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_fact_without_overwriting_it()
    {
        await using var db = CreateDbContext();
        db.Sites.Add(Site.Create("org-001", "env-dev", "SITE-001", "Tenant Site", "UTC"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains("SITE-001", exception.Message, StringComparison.Ordinal);
        Assert.Equal("Tenant Site", (await db.Sites.SingleAsync()).Name);
    }

    [Fact]
    public async Task Ordinary_master_data_seed_does_not_create_leader_demo_facts()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Empty(await db.Sites.Where(x => x.Code == "SITE-001").ToArrayAsync());
        Assert.Empty(await db.Skus.Where(x => x.Code.StartsWith("SKU-DEMO-")).ToArrayAsync());
        Assert.Empty(await db.DeviceAssets.Where(x => x.Code == "DEV-CNC-DEMO").ToArrayAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"master-data-leader-demo-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new MasterDataSeedTestMediator());
    }

    private sealed class MasterDataSeedTestMediator : IMediator
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
