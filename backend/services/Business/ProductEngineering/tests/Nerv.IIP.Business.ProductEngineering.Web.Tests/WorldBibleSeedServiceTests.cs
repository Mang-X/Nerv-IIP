using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》§4 工程主数据的黄金向量：24 成品 × (EBOM + MBOM + 8 工序路线)，
/// 热销 8 款有 V2 版本演进；重复执行幂等，且不触碰固定演示 / 规模块工程事实。
/// </summary>
public sealed class WorldBibleSeedServiceTests
{
    [Fact]
    public void Spec_declares_twenty_four_products_with_eight_to_twelve_bom_lines()
    {
        Assert.Equal(24, WorldBibleSpec.Products.Count);
        Assert.Equal(24, WorldBibleSpec.Products.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, WorldBibleSpec.Products.Count(x => x.IsHotSelling));
        Assert.Equal(8, WorldBibleSpec.StandardOperations.Count);

        Assert.All(WorldBibleSpec.Products, product =>
        {
            Assert.StartsWith("FG-", product.SkuCode, StringComparison.Ordinal);
            Assert.InRange(product.EngineeringLines(WorldBibleSpec.V1Revision).Count, 8, 12);
            Assert.InRange(product.ManufacturingLines(WorldBibleSpec.V1Revision).Count, 8, 12);
            Assert.Equal(8, product.RoutingStages().Count);

            // BOM 行不得重名（唯一索引约束），且必须是设定集号段内的物料。
            var lines = product.ManufacturingLines(WorldBibleSpec.V1Revision);
            Assert.Equal(lines.Count, lines.Select(x => x.ComponentSkuCode).Distinct(StringComparer.Ordinal).Count());
            Assert.All(lines, line => Assert.Matches("^(SF|RM|PK)-", line.ComponentSkuCode));
        });

        // V2 只换弹簧供应商，其余物料不变。
        var hot = WorldBibleSpec.Products.First(x => x.IsHotSelling);
        var v1 = hot.ManufacturingLines(WorldBibleSpec.V1Revision).Select(x => x.ComponentSkuCode).ToArray();
        var v2 = hot.ManufacturingLines(WorldBibleSpec.V2Revision).Select(x => x.ComponentSkuCode).ToArray();
        Assert.Single(v1.Except(v2, StringComparer.Ordinal));
        Assert.Equal([hot.SecondSourceSpringSkuCode], v2.Except(v1, StringComparer.Ordinal));
    }

    [Fact]
    public void Routing_stages_flow_across_the_three_workshops()
    {
        var product = WorldBibleSpec.Products[0];
        var stages = product.RoutingStages();

        Assert.Equal([10, 20, 30, 40, 50, 60, 70, 80], stages.Select(x => x.Operation.Sequence));
        Assert.Equal(
            ["OP-WB-CUT", "OP-WB-CNC", "OP-WB-GRD", "OP-WB-VLV", "OP-WB-ASM", "OP-WB-CTG", "OP-WB-TST", "OP-WB-PKG"],
            stages.Select(x => x.Operation.OperationCode));
        // 前两道在机加车间（按平台确定性分配），后半程固定流转到精磨/阀系/涂装/终检/包装。
        Assert.StartsWith("WC-TUB-", stages[0].WorkCenterCode, StringComparison.Ordinal);
        Assert.StartsWith("WC-ROD-", stages[1].WorkCenterCode, StringComparison.Ordinal);
        Assert.Equal("WC-GRD-01", stages[2].WorkCenterCode);
        Assert.Equal("WC-VA-01", stages[3].WorkCenterCode);
        Assert.Matches("^WC-(FA|RA)-", stages[4].WorkCenterCode);
        Assert.Equal(["WC-CT-01", "WC-TS-01", "WC-PK-01"], stages.Skip(5).Select(x => x.WorkCenterCode));
        Assert.True(stages.Single(x => x.Operation.OperationCode == "OP-WB-TST").Operation.RequiresQualityInspection);
    }

    [Fact]
    public async Task Seed_publishes_every_product_version_once()
    {
        await using var db = CreateDbContext();
        var seed = new WorldBibleSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(8, await db.StandardOperations.CountAsync());
        Assert.Equal(32, await db.EngineeringItems.CountAsync());
        Assert.Equal(32, await db.EngineeringBoms.CountAsync());
        Assert.Equal(32, await db.ManufacturingBoms.CountAsync());
        Assert.Equal(24, await db.Routings.CountAsync());
        Assert.Equal(32, await db.ProductionVersions.CountAsync());

        Assert.All(await db.EngineeringBoms.ToArrayAsync(), bom =>
            Assert.Equal(EngineeringVersionStatus.Published, bom.Status));
        Assert.All(await db.Routings.Include(x => x.Operations).ToArrayAsync(), routing =>
            Assert.Equal(8, routing.Operations.Count));

        // 热销款：V1 在 2026-06-30 失效、V2 自 2026-07-01 起为默认版本。
        var hotSku = WorldBibleSpec.Products.First(x => x.IsHotSelling).SkuCode;
        var hotVersions = await db.ProductionVersions
            .Where(x => x.SkuCode == hotSku)
            .OrderBy(x => x.ValidFrom)
            .ToArrayAsync();
        Assert.Equal(2, hotVersions.Length);
        Assert.Equal(WorldBibleSpec.HotV1ValidTo, hotVersions[0].ValidTo);
        Assert.False(hotVersions[0].IsDefault);
        Assert.Equal(WorldBibleSpec.V2EffectiveDate, hotVersions[1].ValidFrom);
        Assert.Null(hotVersions[1].ValidTo);
        Assert.True(hotVersions[1].IsDefault);

        // 非热销款只有 V1，且是默认版本。
        var plainSku = WorldBibleSpec.Products.First(x => !x.IsHotSelling).SkuCode;
        var plainVersion = Assert.Single(await db.ProductionVersions.Where(x => x.SkuCode == plainSku).ToArrayAsync());
        Assert.True(plainVersion.IsDefault);
        Assert.Null(plainVersion.ValidTo);
    }

    [Fact]
    public async Task Seed_never_touches_the_frozen_demo_engineering_facts()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");
        await new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 1000);

        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Single(await db.ManufacturingBoms.Where(x => x.BomCode == "MBOM-DEMO-001").ToArrayAsync());
        Assert.Single(await db.Routings.Where(x => x.RoutingCode == "ROUTING-DEMO-001").ToArrayAsync());
        Assert.Single(await db.ProductionVersions.Where(x => x.SkuCode == "SKU-DEMO-001").ToArrayAsync());
        Assert.Equal(6, await db.ManufacturingBoms.CountAsync(x => x.BomCode.StartsWith("MBOM-SCALE-")));
        Assert.Equal(6, await db.Routings.CountAsync(x => x.RoutingCode.StartsWith("ROUTING-SCALE-")));
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_tenant_routing_without_overwriting_it()
    {
        await using var db = CreateDbContext();
        var skuCode = WorldBibleSpec.Products[0].SkuCode;
        var routing = Routing.CreateDraft("org-001", "env-dev", WorldBibleSpec.RoutingCode(skuCode), "1", skuCode)
            .AddOperation(10, "WC-OTHER", "OP-OTHER", "租户自维护工序", 5);
        db.Routings.Add(routing);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(WorldBibleSpec.RoutingCode(skuCode), exception.Message, StringComparison.Ordinal);
        var preserved = await db.Routings
            .Include(x => x.Operations)
            .SingleAsync(x => x.RoutingCode == WorldBibleSpec.RoutingCode(skuCode));
        Assert.Equal("OP-OTHER", preserved.Operations.Single().OperationCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"product-engineering-world-bible-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldBibleSeedTestMediator());
    }

    private sealed class WorldBibleSeedTestMediator : IMediator
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
