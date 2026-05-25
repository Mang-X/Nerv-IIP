using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using ErpInfrastructure = Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class ErpPerformanceBaselineTests(ITestOutputHelper output)
{
    [Trait("Category", "erp")]
    [PerformanceBaselineFact("erp")]
    public async Task Erp_sales_order_list_high_read_baseline_outputs_elapsed_rows_scenario_and_profile()
    {
        var settings = PerformanceBaselineSettings.FromEnvironment();
        await using var provider = BusinessPerformanceServiceProvider.CreateErpProvider(settings);
        await BusinessPerformanceServiceProvider.MigrateErpAsync(provider, CancellationToken.None);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpInfrastructure.ApplicationDbContext>();
        var handler = new ListSalesOrdersQueryHandler(db);

        var stopwatch = Stopwatch.StartNew();
        var response = await handler.Handle(
            new ListSalesOrdersQuery(settings.OrganizationId, settings.EnvironmentId),
            CancellationToken.None);
        stopwatch.Stop();

        new PerformanceMetric(
            "erp-sales-order-list-high-read",
            settings.Profile,
            stopwatch.ElapsedMilliseconds,
            response.Items.Count,
            "sales-orders",
            DateTimeOffset.UtcNow).WriteTo(output);
    }
}
