using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class WalkthroughSeedServiceTests
{
    [Fact]
    public async Task Walkthrough_rod_engineering_is_reproducible_without_finished_goods_or_self_dependency()
    {
        await using var db = CreateDbContext();
        var seed = new WalkthroughSeedService(db);
        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var bom = Assert.Single(await db.ManufacturingBoms.Include(x => x.MaterialLines)
            .Where(x => x.SkuCode == "SF-ROD-01").ToArrayAsync());
        Assert.Equal(EngineeringVersionStatus.Published, bom.Status);
        Assert.Equal("EBOM-SF-ROD-01:1", bom.EngineeringBomVersionId);
        var raw = Assert.Single(bom.MaterialLines);
        Assert.Equal("RM-BAR-01", raw.SkuCode);
        Assert.Equal("kg", raw.UnitOfMeasureCode);
        Assert.Equal(1.40m, raw.Quantity * (1 + raw.ScrapRate) / raw.YieldRate);
        var routing = Assert.Single(await db.Routings.Include(x => x.Operations)
            .Where(x => x.SkuCode == "SF-ROD-01").ToArrayAsync());
        Assert.Equal(new[] { "OP-WB-CUT", "OP-WB-CNC", "OP-WB-GRD" },
            routing.Operations.OrderBy(x => x.Sequence).Select(x => x.OperationCode));
        var version = Assert.Single(await db.ProductionVersions.Where(x => x.SkuCode == "SF-ROD-01").ToArrayAsync());
        Assert.Equal("MBOM-SF-ROD-01:1", version.MbomVersionId);
        Assert.Equal("ROUTING-SF-ROD-01:1", version.RoutingVersionId);
        Assert.True(version.IsDefault);
        Assert.True(version.IsResolvableFor(new DateOnly(2026, 9, 7), 1m));
    }

    [Fact]
    public async Task Walkthrough_projection_seeds_one_auditable_product_and_its_work_standards()
    {
        await using var db = CreateDbContext();
        var seed = new WalkthroughSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(8, await db.StandardOperations.CountAsync());
        Assert.Equal(3, await db.EngineeringItems.CountAsync());
        Assert.Equal(3, await db.EngineeringBoms.CountAsync());
        Assert.Equal(3, await db.ManufacturingBoms.CountAsync());
        var routing = Assert.Single(await db.Routings.Include(x => x.Operations)
            .Where(x => x.SkuCode == WalkthroughSeedSpec.FinishedSkuCode).ToArrayAsync());
        Assert.Equal(WalkthroughSeedSpec.FinishedSkuCode, routing.SkuCode);
        Assert.Equal(8, routing.Operations.Count);
        Assert.All(routing.Operations, operation => Assert.True(operation.RunMinutes > 0));
        Assert.Equal(3, await db.ProductionVersions.CountAsync());
    }

    [Theory]
    [InlineData("RM-BAR-01", 2.8)]
    [InlineData("SF-ROD-01", 1.4)]
    [InlineData("FG-QJ-P1-L", 1.4)]
    public async Task Walkthrough_rejects_incompatible_reserved_rod_material_without_overwriting(string component, decimal quantity)
    {
        await using var db = CreateDbContext();
        var mbom = Domain.AggregatesModel.ManufacturingBomAggregate.ManufacturingBom
            .CreateDraft("org-001", "env-dev", "MBOM-SF-ROD-01", "1", "SF-ROD-01")
            .AddMaterialLine(component, quantity, "kg", 0m);
        mbom.ReleaseFromEngineeringBom("EBOM-SF-ROD-01:1", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 5));
        db.ManufacturingBoms.Add(mbom);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WalkthroughSeedService(db).SeedAsync("org-001", "env-dev"));
        db.ChangeTracker.Clear();
        var preserved = await db.ManufacturingBoms.Include(x => x.MaterialLines)
            .SingleAsync(x => x.SkuCode == "SF-ROD-01");
        var line = Assert.Single(preserved.MaterialLines);
        Assert.Equal(component, line.SkuCode);
        Assert.Equal(quantity, line.Quantity);
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_tenant_routing_without_overwriting_it()
    {
        await using var db = CreateDbContext();
        var skuCode = WalkthroughSeedSpec.FinishedSkuCode;
        var routing = Routing.CreateDraft("org-001", "env-dev", WalkthroughSeedSpec.RoutingCode(skuCode), "1", skuCode)
            .AddOperation(10, "WC-OTHER", "OP-OTHER", "租户自维护工序", 5);
        db.Routings.Add(routing);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WalkthroughSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(WalkthroughSeedSpec.RoutingCode(skuCode), exception.Message, StringComparison.Ordinal);
        var preserved = await db.Routings
            .Include(x => x.Operations)
            .SingleAsync(x => x.RoutingCode == WalkthroughSeedSpec.RoutingCode(skuCode));
        Assert.Equal("OP-OTHER", preserved.Operations.Single().OperationCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"product-engineering-walkthrough-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WalkthroughSeedTestMediator());
    }

    private sealed class WalkthroughSeedTestMediator : IMediator
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
