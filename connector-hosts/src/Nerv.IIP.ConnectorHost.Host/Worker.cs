namespace Nerv.IIP.ConnectorHost.Host;

public class Worker(ILogger<Worker> logger, Application.ConnectorReportingLoop reportingLoop) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reportingLoop.RunCycleAsync(stoppingToken);
                logger.LogInformation("Connector Host reporting cycle completed at {time}", DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector Host reporting cycle failed and will be retried.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
