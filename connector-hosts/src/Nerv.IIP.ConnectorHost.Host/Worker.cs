namespace Nerv.IIP.ConnectorHost.Host;

public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    Application.ConnectorReportingLoop reportingLoop,
    Application.ConnectorOperationLoop operationLoop,
    IReadOnlyList<Connectors.OpcUa.IOpcUaCollectionConnector> opcUaCollectors) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cycleSeconds = configuration.GetValue("ConnectorHost:CycleSeconds", 30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var collector in opcUaCollectors)
                {
                    await collector.RunCollectionCycleAsync(stoppingToken);
                }

                await reportingLoop.RunCycleAsync(stoppingToken);
                await operationLoop.RunCycleAsync(stoppingToken);
                logger.LogInformation("Connector Host cycle completed at {time}", DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector Host cycle failed and will be retried.");
            }

            await Task.Delay(TimeSpan.FromSeconds(cycleSeconds), stoppingToken);
        }
    }
}
