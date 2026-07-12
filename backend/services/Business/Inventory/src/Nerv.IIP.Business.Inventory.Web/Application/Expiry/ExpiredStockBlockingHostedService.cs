using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class ExpiredStockBlockingHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExpiredStockBlockingOptions> options,
    ILogger<ExpiredStockBlockingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await RunOnceAsync(stoppingToken);
        var interval = options.Value.Interval > TimeSpan.Zero
            ? options.Value.Interval
            : TimeSpan.FromMinutes(30);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExpiredStockBlockingService>();
            var count = await service.BlockExpiredAvailableStockAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
            if (count > 0)
            {
                logger.LogInformation("Blocked {Count} expired Inventory ledger lines.", count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Expired Inventory stock blocking worker failed.");
        }
    }
}
