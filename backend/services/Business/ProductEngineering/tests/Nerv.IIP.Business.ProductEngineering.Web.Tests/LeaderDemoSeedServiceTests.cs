using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class LeaderDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_published_engineering_prerequisites_once()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var mbom = Assert.Single(await db.ManufacturingBoms.Include(x => x.MaterialLines).ToArrayAsync());
        Assert.Equal("Published", mbom.Status.ToString());
        Assert.Equal("SKU-DEMO-RM-001", Assert.Single(mbom.MaterialLines).SkuCode);
        var routing = Assert.Single(await db.Routings.Include(x => x.Operations).ToArrayAsync());
        Assert.Equal("Published", routing.Status.ToString());
        Assert.True(Assert.Single(routing.Operations).RequiresQualityInspection);
        var version = Assert.Single(await db.ProductionVersions.ToArrayAsync());
        Assert.Equal("active", version.Status);
        Assert.Equal(LeaderDemoSeedService.MbomVersionId, version.MbomVersionId);
        Assert.Equal(LeaderDemoSeedService.RoutingVersionId, version.RoutingVersionId);
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_mbom()
    {
        await using var db = CreateDbContext();
        db.ManufacturingBoms.Add(ManufacturingBom.CreateDraft("org-001", "env-dev", LeaderDemoSeedService.MbomCode, LeaderDemoSeedService.Revision, "OTHER-SKU"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(LeaderDemoSeedService.MbomVersionId, exception.Message, StringComparison.Ordinal);
        Assert.Equal("OTHER-SKU", (await db.ManufacturingBoms.SingleAsync()).SkuCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"product-engineering-leader-demo-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new ProductEngineeringSeedTestMediator());
    }

    private sealed class ProductEngineeringSeedTestMediator : IMediator
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
