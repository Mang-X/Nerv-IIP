using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class ExpiredStockReservationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<StockReservationExpirationOptions> options,
    ILogger<ExpiredStockReservationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await RunOnceAsync(stoppingToken);
        var interval = options.Value.ScanInterval > TimeSpan.Zero
            ? options.Value.ScanInterval
            : TimeSpan.FromMinutes(1);
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
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expirationService = scope.ServiceProvider.GetRequiredService<ExpiredStockReservationService>();
            var metrics = scope.ServiceProvider.GetRequiredService<InventoryReservationMetrics>();
            var expiredCount = await expirationService.ExpireOpenReservationsAsync(DateTime.UtcNow, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await metrics.RefreshOpenReservationsAsync(dbContext, cancellationToken);
            metrics.RecordExpiration(expiredCount);
            if (expiredCount > 0)
            {
                logger.LogInformation("Released {ExpiredReservationCount} expired Inventory stock reservations.", expiredCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory reservation expiration worker failed.");
        }
    }
}
