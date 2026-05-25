using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using InventoryInfrastructure = Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class InventoryPerformanceBaselineTests(ITestOutputHelper output)
{
    [Trait("Category", "inventory")]
    [PerformanceBaselineFact("inventory")]
    public async Task Inventory_high_write_baseline_outputs_elapsed_rows_scenario_and_profile()
    {
        var settings = PerformanceBaselineSettings.FromEnvironment();
        await using var provider = BusinessPerformanceServiceProvider.CreateInventoryProvider(settings);
        await BusinessPerformanceServiceProvider.MigrateInventoryAsync(provider, CancellationToken.None);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryInfrastructure.ApplicationDbContext>();
        var handler = new PostStockMovementCommandHandler(db);
        var runId = Guid.CreateVersion7().ToString("N");

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < settings.SampleRows; i++)
        {
            await handler.Handle(
                new PostStockMovementCommand(
                    settings.OrganizationId,
                    settings.EnvironmentId,
                    "inbound",
                    "performance-baseline",
                    $"perf-{runId}",
                    i.ToString("D6"),
                    $"{runId}-{i:D6}",
                    "SKU-PERF",
                    "pcs",
                    "SITE-PERF",
                    "LOC-PERF",
                    null,
                    null,
                    "qualified",
                    "company",
                null,
                1m),
                CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        stopwatch.Stop();
        new PerformanceMetric(
            "inventory-high-write",
            settings.Profile,
            stopwatch.ElapsedMilliseconds,
            settings.SampleRows,
            "stock-movements",
            DateTimeOffset.UtcNow).WriteTo(output);
    }
}
