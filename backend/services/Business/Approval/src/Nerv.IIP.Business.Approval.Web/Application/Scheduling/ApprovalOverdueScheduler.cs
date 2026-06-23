using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

namespace Nerv.IIP.Business.Approval.Web.Application.Scheduling;

public sealed class ApprovalOverdueScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ApprovalOverdueScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Approval:OverdueCheck:Enabled"))
        {
            return;
        }

        var organizationId = configuration["Approval:OverdueCheck:OrganizationId"];
        var environmentId = configuration["Approval:OverdueCheck:EnvironmentId"];
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            logger.LogWarning("Approval overdue check is enabled but OrganizationId or EnvironmentId is missing.");
            return;
        }

        var interval = configuration.GetValue("Approval:OverdueCheck:Interval", DefaultInterval);
        if (interval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "Approval overdue check interval {Interval} is not positive; falling back to {DefaultInterval}.",
                interval,
                DefaultInterval);
            interval = DefaultInterval;
        }

        using var timer = new PeriodicTimer(interval);
        await TryCheckAsync(organizationId, environmentId, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryCheckAsync(organizationId, environmentId, stoppingToken);
        }
    }

    private async Task TryCheckAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var marked = await sender.Send(new CheckOverdueApprovalStepsCommand(organizationId, environmentId), cancellationToken);
            if (marked > 0)
            {
                logger.LogInformation(
                    "Marked {MarkedCount} overdue approval steps for {OrganizationId}/{EnvironmentId}.",
                    marked,
                    organizationId,
                    environmentId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Approval overdue check failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                organizationId,
                environmentId);
        }
    }
}
