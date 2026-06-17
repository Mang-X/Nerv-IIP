using Nerv.IIP.Business.Maintenance.Web.Application.Commands;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;

public sealed class MaintenancePlanDueScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MaintenancePlanDueScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Maintenance:PmGeneration:Enabled"))
        {
            return;
        }

        var organizationId = configuration["Maintenance:PmGeneration:OrganizationId"];
        var environmentId = configuration["Maintenance:PmGeneration:EnvironmentId"];
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            logger.LogWarning("Maintenance PM generation is enabled but OrganizationId or EnvironmentId is missing.");
            return;
        }

        var interval = configuration.GetValue("Maintenance:PmGeneration:Interval", DefaultInterval);
        using var timer = new PeriodicTimer(interval);
        await TryGenerateAsync(organizationId, environmentId, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryGenerateAsync(organizationId, environmentId, stoppingToken);
        }
    }

    private async Task TryGenerateAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        try
        {
            await GenerateAsync(organizationId, environmentId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Maintenance PM generation failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                organizationId,
                environmentId);
        }
    }

    private async Task GenerateAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await sender.Send(
            new GenerateDueMaintenanceWorkOrdersCommand(organizationId, environmentId, businessDate, "system:pm-scheduler"),
            cancellationToken);
        if (result.GeneratedCount > 0)
        {
            logger.LogInformation(
                "Generated {GeneratedCount} due maintenance work orders for {OrganizationId}/{EnvironmentId} on {BusinessDate}.",
                result.GeneratedCount,
                organizationId,
                environmentId,
                businessDate);
        }
    }
}
