namespace Nerv.IIP.ConnectorHost.Host;

public class Worker(
    ILogger<Worker> logger,
    ConnectorHostWorkerOptions options,
    TimeProvider timeProvider,
    Application.ConnectorReportingLoop reportingLoop,
    Application.ConnectorManifestReportingLoop manifestReportingLoop,
    Application.ConnectorOperationLoop operationLoop,
    IndustrialTelemetryCollectorRunner telemetryCollectorRunner,
    IReadOnlyList<Connectors.Abstractions.IIndustrialTelemetryCollectionConnector> telemetryCollectors,
    IReadOnlyList<Connectors.Abstractions.IConnectorConnectionMonitor> connectionMonitors,
    Application.IConnectorReportSignal reportSignal,
    Application.IConnectorManifestSignal manifestSignal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        options.Validate();
        var workers = telemetryCollectors
            .Select(collector => RunCollectionLoopAsync(collector, stoppingToken))
            .Concat(connectionMonitors.Select(monitor => RunConnectionMonitorLoopAsync(monitor, stoppingToken)))
            .Append(RunReportingLoopAsync(stoppingToken))
            .Append(RunManifestReportingLoopAsync(stoppingToken))
            .Append(RunOperationLoopAsync(stoppingToken));

        await Task.WhenAll(workers);
    }

    private async Task RunCollectionLoopAsync(
        Connectors.Abstractions.IIndustrialTelemetryCollectionConnector collector,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunIsolatedAsync(
                () => telemetryCollectorRunner.RunCollectionCycleAsync([collector], cancellationToken),
                "Industrial telemetry collection cycle failed and will be retried.",
                cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(options.CollectionCycleSeconds), timeProvider, cancellationToken);
        }
    }

    private async Task RunConnectionMonitorLoopAsync(
        Connectors.Abstractions.IConnectorConnectionMonitor monitor,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.ConnectionProbeSeconds),
            timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RunIsolatedAsync(
                () => monitor.RunConnectionCheckAsync(cancellationToken),
                "Connector connection check failed and will be retried.",
                cancellationToken);
        }
    }

    private async Task RunReportingLoopAsync(CancellationToken cancellationToken)
    {
        string? changedConnectorId = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunIsolatedAsync(
                () => reportingLoop.RunCycleAsync(cancellationToken, changedConnectorId),
                "Connector reporting cycle failed and will be retried.",
                cancellationToken);
            changedConnectorId = await reportSignal.WaitAsync(
                TimeSpan.FromSeconds(options.HeartbeatSeconds),
                timeProvider,
                cancellationToken);
        }
    }

    private async Task RunOperationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunIsolatedAsync(
                () => operationLoop.RunCycleAsync(cancellationToken),
                "Connector operation polling cycle failed and will be retried.",
                cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(options.OperationPollSeconds), timeProvider, cancellationToken);
        }
    }

    private async Task RunManifestReportingLoopAsync(CancellationToken cancellationToken)
    {
        Application.ConnectorManifestSignalEvent? signal = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset? nextAttemptAtUtc = null;
            try
            {
                nextAttemptAtUtc = await manifestReportingLoop.RunCycleAsync(
                    cancellationToken,
                    signal is { ForceRebirth: true } ? signal.ConnectorId : null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Connector manifest reporting cycle failed and will be retried.");
            }

            var wait = nextAttemptAtUtc.HasValue
                ? nextAttemptAtUtc.Value - timeProvider.GetUtcNow()
                : TimeSpan.FromSeconds(options.HeartbeatSeconds);
            if (wait <= TimeSpan.Zero)
            {
                wait = TimeSpan.FromMilliseconds(1);
            }

            signal = await manifestSignal.WaitAsync(wait, timeProvider, cancellationToken);
        }
    }

    private async Task RunIsolatedAsync(
        Func<Task> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, failureMessage);
        }
    }
}
