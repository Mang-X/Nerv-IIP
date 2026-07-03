using Microsoft.Extensions.Options;

namespace Nerv.IIP.Notification.Web.Application.Notifications;

internal sealed class NotificationDeliveryRetryWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<NotificationDeliveryOptions> options,
    ILogger<NotificationDeliveryRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = options.Value;
        if (!currentOptions.RetryWorkerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(currentOptions.RetryPollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var deliveryService = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();
                var dispatched = await deliveryService.DispatchDueAttemptsAsync(timeProvider.GetUtcNow(), stoppingToken);
                if (dispatched > 0)
                {
                    logger.LogInformation("Dispatched {DeliveryAttemptCount} notification delivery attempts.", dispatched);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Notification delivery retry worker tick failed.");
            }
        }
    }
}
