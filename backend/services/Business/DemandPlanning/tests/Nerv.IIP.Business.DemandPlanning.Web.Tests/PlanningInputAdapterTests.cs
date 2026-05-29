using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class PlanningInputAdapterTests
{
    [Fact]
    public async Task Fixture_adapter_returns_snapshots_without_cross_service_table_access()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await new DemandPlanningFixtureInputSnapshotProvider(dbContext).GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("fixture-production-engineering-snapshot", snapshot.ProductionEngineeringSnapshotSource);
        Assert.Equal("fixture-inventory-availability-snapshot", snapshot.InventorySnapshotSource);
        Assert.Single(snapshot.Demands);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000" && x.AvailableQuantity == 2m);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.ClrType.FullName?.Contains("ProductEngineering", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.ClrType.FullName?.Contains("Inventory", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Upstream_adapter_uses_product_engineering_and_inventory_snapshots_for_mrp_inputs()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var engineering = new FakePlanningProductEngineeringClient();
        var inventory = new FakePlanningInventoryClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(dbContext, engineering, inventory);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("product-engineering-http:2", snapshot.ProductionEngineeringSnapshotSource);
        Assert.Equal("inventory-http:2", snapshot.InventorySnapshotSource);
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000" && x.ProductionVersionReference == "PV-REAL-001");
        Assert.Contains(snapshot.BomComponents, x => x.ParentSkuCode == "SKU-FG-1000" && x.ComponentSkuCode == "SKU-RM-1000" && x.QuantityPerParent == 3m);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000" && x.AvailableQuantity == 2m);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-RM-1000" && x.AvailableQuantity == 5m);
        Assert.Equal(["SKU-FG-1000"], engineering.RequestedParentSkuCodes);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], inventory.RequestedSkuCodes);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"demand-planning-adapter-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private sealed class FakePlanningProductEngineeringClient : IPlanningProductEngineeringSnapshotClient
    {
        public IReadOnlyCollection<string> RequestedParentSkuCodes { get; private set; } = [];

        public Task<PlanningProductEngineeringSnapshot> GetSnapshotAsync(
            string internalBearerToken,
            PlanningProductEngineeringSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedParentSkuCodes = request.ParentSkuCodes;
            return Task.FromResult(new PlanningProductEngineeringSnapshot(
                "product-engineering-http:2",
                [
                    new ProductionVersionSnapshot("SKU-FG-1000", "PV-REAL-001", "MBOM-REAL-001", "ROUTING-REAL-001"),
                ],
                [
                    new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m),
                ]));
        }
    }

    private sealed class FakePlanningInventoryClient : IPlanningInventorySnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningInventorySnapshot> GetAvailabilitySnapshotAsync(
            string internalBearerToken,
            PlanningInventorySnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningInventorySnapshot(
                "inventory-http:2",
                [
                    new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                    new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
                ]));
        }
    }
}
