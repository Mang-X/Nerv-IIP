using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class LeaderDemoScaleSeedServiceTests
{
    [Fact]
    public async Task Scale_seed_publishes_one_four_operation_routing_and_active_version_per_scale_sku_once()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoScaleSeedService(db);

        await seed.SeedAsync("org-001", "env-dev", 1000);
        await seed.SeedAsync("org-001", "env-dev", 1000);

        Assert.Equal(6, await db.ManufacturingBoms.CountAsync(x => x.BomCode.StartsWith("MBOM-SCALE-")));
        var routings = await db.Routings
            .Include(x => x.Operations)
            .Where(x => x.RoutingCode.StartsWith("ROUTING-SCALE-"))
            .ToArrayAsync();
        Assert.Equal(6, routings.Length);
        Assert.All(routings, routing =>
        {
            var operations = routing.Operations.OrderBy(x => x.Sequence).ToArray();
            Assert.Equal([10, 20, 30, 40], operations.Select(x => x.Sequence));
            Assert.Equal(
                ["WC-SCALE-WELD", "WC-SCALE-ROD", "WC-SCALE-SEAL", "WC-SCALE-TEST"],
                operations.Select(x => x.WorkCenterCode));
            // 规模块工序一律不要求质检，否则 BusinessScheduling 会把整批工序直接判为 quality 不可排。
            Assert.All(operations, operation => Assert.False(operation.RequiresQualityInspection));
        });

        var versions = await db.ProductionVersions
            .Where(x => x.SkuCode.StartsWith("SKU-SCALE-") && x.Status == ProductionVersionStatus.Active)
            .ToArrayAsync();
        Assert.Equal(6, versions.Length);
        Assert.All(versions, version =>
        {
            Assert.True(version.IsDefault);
            Assert.Null(version.ValidTo);
        });
    }

    [Fact]
    public async Task Scale_seed_is_disabled_when_the_configured_order_count_is_not_positive()
    {
        await using var db = CreateDbContext();

        await new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 0);

        Assert.Empty(await db.Routings.ToArrayAsync());
        Assert.Empty(await db.ManufacturingBoms.ToArrayAsync());
        Assert.Empty(await db.ProductionVersions.ToArrayAsync());
    }

    [Fact]
    public async Task Scale_seed_leaves_the_frozen_leader_demo_engineering_facts_untouched()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");

        await new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 250);

        Assert.Single(await db.Routings.Where(x => x.RoutingCode == "ROUTING-DEMO-001").ToArrayAsync());
        Assert.Single(await db.ManufacturingBoms.Where(x => x.BomCode == "MBOM-DEMO-001").ToArrayAsync());
        Assert.Single(await db.ProductionVersions.Where(x => x.SkuCode == "SKU-DEMO-001").ToArrayAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"product-engineering-leader-demo-scale-{Guid.CreateVersion7():N}")
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
