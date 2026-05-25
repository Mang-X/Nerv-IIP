using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using MesInfrastructure = Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class MesPerformanceBaselineTests(ITestOutputHelper output)
{
    [Trait("Category", "mes")]
    [PerformanceBaselineFact("mes")]
    public async Task Mes_work_order_high_read_baseline_outputs_elapsed_rows_scenario_and_profile()
    {
        var settings = PerformanceBaselineSettings.FromEnvironment();
        await using var provider = BusinessPerformanceServiceProvider.CreateMesProvider(settings);
        await BusinessPerformanceServiceProvider.MigrateMesAsync(provider, CancellationToken.None);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesInfrastructure.ApplicationDbContext>();
        var handler = new ListMesWorkOrdersQueryHandler(db);

        var stopwatch = Stopwatch.StartNew();
        var response = await handler.Handle(
            new ListMesWorkOrdersQuery(settings.OrganizationId, settings.EnvironmentId, null, settings.SampleRows),
            CancellationToken.None);
        stopwatch.Stop();

        new PerformanceMetric(
            "mes-work-order-high-read",
            settings.Profile,
            stopwatch.ElapsedMilliseconds,
            response.Items.Count,
            "work-orders",
            DateTimeOffset.UtcNow).WriteTo(output);
    }
}
