using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public sealed class OrderUrgencyRefreshWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderUrgencyRefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Periodic order-urgency recalculation failed.");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var urgencyService = scope.ServiceProvider.GetRequiredService<OrderUrgencyService>();
        var contexts = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Select(x => new { x.OrganizationId, x.EnvironmentId })
            .Distinct()
            .ToArrayAsync(cancellationToken);
        foreach (var context in contexts)
        {
            await urgencyService.RefreshContextAsync(context.OrganizationId, context.EnvironmentId, cancellationToken);
        }
    }
}
