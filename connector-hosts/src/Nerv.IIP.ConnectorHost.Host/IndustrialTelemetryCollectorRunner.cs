using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Host;

public sealed class IndustrialTelemetryCollectorRunner(ILogger<IndustrialTelemetryCollectorRunner> logger)
{
    public async Task RunCollectionCycleAsync(
        IReadOnlyList<IIndustrialTelemetryCollectionConnector> collectors,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(collectors.Select(collector => RunCollectorAsync(collector, cancellationToken)));
    }

    private async Task RunCollectorAsync(
        IIndustrialTelemetryCollectionConnector collector,
        CancellationToken cancellationToken)
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
