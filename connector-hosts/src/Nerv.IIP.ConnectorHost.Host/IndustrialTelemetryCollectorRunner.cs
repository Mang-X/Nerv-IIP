using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Host;

public sealed class IndustrialTelemetryCollectorRunner(ILogger<IndustrialTelemetryCollectorRunner> logger)
{
    public async Task RunCollectionCycleAsync(
        IReadOnlyList<IIndustrialTelemetryCollectionConnector> collectors,
        CancellationToken cancellationToken)
    {
        foreach (var collector in collectors)
        {
            try
            {
                await collector.RunCollectionCycleAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Industrial telemetry collector cycle failed; continuing with remaining collectors.");
            }
        }
    }
}
