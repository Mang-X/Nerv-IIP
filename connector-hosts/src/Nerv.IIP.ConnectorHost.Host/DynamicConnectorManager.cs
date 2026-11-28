using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Modbus;
using Nerv.IIP.ConnectorHost.Connectors.Mqtt;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.ConnectorHost.Host;

public sealed class DynamicConnectorManager(
    HttpConnectorDesiredConfigClient desiredClient,
    ConnectorHostRuntimeContext context,
    ConnectorHostWorkerOptions options,
    ConnectorTargetSnapshotStore snapshots,
    IndustrialTelemetryCollectorRunner collection,
    IIndustrialTelemetrySamplesClient samples,
    IConnectorReportSignal reportSignal,
    IConnectorManifestSignal manifestSignal,
    TimeProvider timeProvider,
    ILogger<DynamicConnectorManager> logger) : BackgroundService
{
    private readonly Dictionary<string, RunningConnector> running = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);
        try
        {
            do
            {
                try
                {
                    await ReconcileAsync(await desiredClient.GetAsync(stoppingToken), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Could not reconcile desired telemetry connectors.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            await Task.WhenAll(running.Values.Select(StopAsync));
        }
    }

    internal async Task ReconcileAsync(IReadOnlyList<TelemetryConnectorConfigItem> desired, CancellationToken stoppingToken)
    {
        var revisions = desired.ToDictionary(x => x.ConnectorId, x => x.Revision, StringComparer.Ordinal);
        foreach (var (id, entry) in running.ToArray())
        {
            if (!revisions.TryGetValue(id, out var revision) || revision != entry.Revision)
            {
                await StopAsync(entry);
                running.Remove(id);
            }
        }

        foreach (var config in desired.Where(x => !running.ContainsKey(x.ConnectorId)))
        {
            var (connector, client) = Create(config);
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            snapshots.Add(connector);
            var task = CollectAsync((IIndustrialTelemetryCollectionConnector)connector, config.PollIntervalSeconds, lifetime.Token);
            running.Add(config.ConnectorId, new(config.Revision, connector, client, lifetime, task));
            reportSignal.Signal(config.ConnectorId);
            manifestSignal.Signal(config.ConnectorId);
        }
    }

    private async Task CollectAsync(IIndustrialTelemetryCollectionConnector connector, int pollSeconds, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await collection.RunCollectionCycleAsync([connector], ct);
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), timeProvider, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task StopAsync(RunningConnector entry)
    {
        await entry.Lifetime.CancelAsync();
        await snapshots.RemoveAsync(entry.Connector);
        await entry.Task;
        entry.Client.Dispose();
        entry.Lifetime.Dispose();
    }

    private (IConnector Connector, IDisposable Client) Create(TelemetryConnectorConfigItem config)
    {
        var timeout = TimeSpan.FromSeconds(options.ConnectionDetectionBudgetSeconds);
        var (id, host, org, env) = (config.ConnectorId, context.ConnectorHostId, context.OrganizationId, context.EnvironmentId);
        if (config.Protocol == "modbus")
        {
            var client = new ModbusTcpClient(timeout);
            var mappings = config.Mappings.Select(mapping => new ModbusRegisterMapping(
                mapping.DeviceAssetId, mapping.TagKey, mapping.UnitId, ModbusRegisterTable.HoldingRegisters,
                mapping.Address, (ushort)(mapping.DataType == "Float32" ? 2 : 1), 1, 0, mapping.BucketSeconds,
                Enum.Parse<ModbusRegisterDataType>(mapping.DataType))).ToArray();
            var connector = new ModbusConnector(new(id, host, org, env, config.Endpoint, null, mappings, CollectionConnectorId: id),
                client, samples, timeProvider: timeProvider, reportSignal: reportSignal, manifestSignal: manifestSignal);
            return (connector, client);
        }

        if (config.Protocol == "mqtt")
        {
            var client = new MqttNetSubscriptionClient(new EnvironmentMqttCredentialResolver(), timeout);
            var mappings = config.Mappings.Select(mapping => new MqttTopicMapping(
                mapping.DeviceAssetId, mapping.TagKey, mapping.TopicFilter!, mapping.ValueJsonPath!, mapping.BucketSeconds)).ToArray();
            var connector = new MqttConnector(new(id, host, org, env, config.Endpoint, $"{host}-{id}", null, mappings, CollectionConnectorId: id),
                client, samples, timeProvider: timeProvider, reportSignal: reportSignal, manifestSignal: manifestSignal);
            return (connector, client);
        }

        var opcClient = new OpcUaNetStandardClient(new EnvironmentOpcUaCredentialResolver(), timeout);
        var tags = config.Mappings.Select(mapping => new OpcUaTagSubscription(
            mapping.DeviceAssetId, mapping.TagKey, mapping.NodeId!, config.PollIntervalSeconds * 1000, mapping.BucketSeconds)).ToArray();
        var opcConnector = new OpcUaConnector(new(id, host, org, env, config.Endpoint, "None", "None", null, "ns=0;i=85", tags, CollectionConnectorId: id),
            opcClient, samples, timeProvider: timeProvider, reportSignal: reportSignal, manifestSignal: manifestSignal);
        return (opcConnector, opcClient);
    }

    private sealed record RunningConnector(Guid Revision, IConnector Connector, IDisposable Client, CancellationTokenSource Lifetime, Task Task);
}
