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

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"demand-planning-adapter-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
