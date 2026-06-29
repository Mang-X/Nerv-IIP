using Microsoft.Extensions.Options;
using Nerv.IIP.Ops.Infrastructure.Repositories;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed class OperationLeaseReaperOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int ScopeBatchSize { get; set; } = 25;
    public int LeaseBatchSize { get; set; } = 50;
}

public sealed class OperationLeaseReaperHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OperationLeaseReaperOptions> options,
    ILogger<OperationLeaseReaperHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Ops lease reaper is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.IntervalSeconds, 5, 3600));
            await Task.Delay(interval, stoppingToken);
            await ReapOnceAsync(stoppingToken);
        }
    }

    private async Task ReapOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOperationTaskRepository>();
            var reaper = scope.ServiceProvider.GetRequiredService<IOperationLeaseReaper>();
            var now = DateTimeOffset.UtcNow;
            var scopes = await repository.GetExpiredLeaseScopesAsync(
                Math.Clamp(options.Value.ScopeBatchSize, 1, 100),
                now,
                cancellationToken);

            foreach (var leaseScope in scopes)
            {
                var result = await reaper.ReapExpiredLeasesAsync(
                    leaseScope.OrganizationId,
                    leaseScope.EnvironmentId,
                    now,
                    Math.Clamp(options.Value.LeaseBatchSize, 1, 100),
                    cancellationToken);
                if (result.RequeuedCount > 0 || result.FailedCount > 0)
                {
                    logger.LogInformation(
                        "Ops lease reaper reclaimed expired leases for OrganizationId={OrganizationId} EnvironmentId={EnvironmentId} Requeued={Requeued} Failed={Failed}",
                        leaseScope.OrganizationId,
                        leaseScope.EnvironmentId,
                        result.RequeuedCount,
                        result.FailedCount);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ops lease reaper failed.");
        }
    }
}
