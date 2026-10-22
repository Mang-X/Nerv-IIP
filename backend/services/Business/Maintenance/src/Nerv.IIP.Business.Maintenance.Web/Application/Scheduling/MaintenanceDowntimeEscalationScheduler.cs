using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;

public sealed class MaintenanceDowntimeEscalationScheduler(IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceDowntimeEscalationOptions> options, TimeProvider timeProvider,
    ILogger<MaintenanceDowntimeEscalationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Scopes.Count == 0)
        {
            logger.LogInformation("Maintenance downtime escalation has no configured scopes or planner recipients; no scan will run.");
            return;
        }

        using var timer = new PeriodicTimer(options.Value.ScanInterval, timeProvider);
        do
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MaintenanceDowntimeEscalationScanner>().ScanAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
