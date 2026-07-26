using System.Globalization;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

public sealed class SimulatedConnector :
    IConnector,
    IIndustrialTelemetryCollectionConnector,
    IConnectorConnectionMonitor,
    IConnectorOperationExecutor
{
    private readonly SimulatedConnectorOptions _options;
    private readonly ConnectorHostRuntimeContext _runtimeContext;
    private readonly IIndustrialTelemetrySamplesClient _samplesClient;
    private readonly TimeProvider _timeProvider;
    private readonly IConnectorReportSignal? _reportSignal;
    private readonly SimulatedScenarioEvaluator _evaluator;
    private readonly IReadOnlyList<SimulatedConnectorRuntime> _runtimes;
    private readonly SimulatedCommandRouter _commandRouter;

    public SimulatedConnector(
        SimulatedConnectorOptions options,
        ConnectorHostRuntimeContext runtimeContext,
        IIndustrialTelemetrySamplesClient samplesClient,
        TimeProvider timeProvider,
        IConnectorReportSignal? reportSignal = null,
        IConnectorManifestSignal? manifestSignal = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _samplesClient = samplesClient ?? throw new ArgumentNullException(nameof(samplesClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _reportSignal = reportSignal;
        _evaluator = new SimulatedScenarioEvaluator(options);
        _runtimes = options.Connectors
            .Select(profile => new SimulatedConnectorRuntime(
                profile,
                options,
                timeProvider,
                reportSignal,
                manifestSignal))
            .ToArray();
        var runtimesById = _runtimes.ToDictionary(
            runtime => runtime.Profile.ConnectorId,
            StringComparer.Ordinal);
        _commandRouter = new SimulatedCommandRouter(
            runtimeContext,
            runtimesById,
            timeProvider,
            reportSignal,
            options.CommandReceiptCacheCapacity);
    }

    public IReadOnlyDictionary<string, int> PendingSampleCounts =>
        _runtimes.ToDictionary(
            runtime => runtime.Profile.ConnectorId,
            runtime => runtime.Outbox.Count,
            StringComparer.Ordinal);

    public int CommandReceiptCount => _commandRouter.ReceiptCount;
    public IReadOnlyList<string> CachedOperationTaskIds => _commandRouter.CachedOperationTaskIds;

    public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ConnectorTarget> targets = _runtimes
            .Select(CreateTarget)
            .ToArray();
        return Task.FromResult(targets);
    }

    public async Task RunCollectionCycleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nowUtc = _timeProvider.GetUtcNow();
        var cycle = ResolveCycle(nowUtc);
        foreach (var runtime in _runtimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerateCycle(runtime, nowUtc, cycle);
            await DeliverDueAsync(runtime, nowUtc, cancellationToken);
        }
    }

    public Task RunConnectionCheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var runtime in _runtimes)
        {
            runtime.ConnectionTracker.MarkAlive();
        }

        return Task.CompletedTask;
    }

    public bool CanExecute(OperationTaskDispatchItem task) =>
        _commandRouter.CanExecute(task);

    public Task<ConnectorOperationExecution> ExecuteAsync(
        OperationTaskDispatchItem task,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_commandRouter.Execute(task));
    }

    private long ResolveCycle(DateTimeOffset nowUtc)
    {
        var elapsed = nowUtc - _options.EpochUtc;
        if (elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        return elapsed.Ticks / TimeSpan.FromMilliseconds(
            _options.SampleIntervalMilliseconds).Ticks;
    }

    private void GenerateCycle(
        SimulatedConnectorRuntime runtime,
        DateTimeOffset nowUtc,
        long cycle)
    {
        foreach (var device in runtime.Profile.Devices)
        {
            foreach (var tag in device.Tags)
            {
                var identity = (device.DeviceAssetId, tag.TagKey);
                lock (runtime.Gate)
                {
                    if (runtime.LastGeneratedCycles.TryGetValue(identity, out var generated)
                        && generated >= cycle)
                    {
                        continue;
                    }

                    runtime.LastGeneratedCycles[identity] = cycle;
                }

                decimal? controlledValue;
                string deviceState;
                DateTimeOffset stateOccurredAtUtc;
                lock (runtime.Gate)
                {
                    runtime.ControlledValues.TryGetValue(identity, out controlledValue);
                    deviceState = runtime.DeviceStates[device.DeviceAssetId].State;
                    stateOccurredAtUtc = runtime.DeviceStates[device.DeviceAssetId].OccurredAtUtc;
                }

                var sample = _evaluator.Evaluate(tag, nowUtc, cycle, controlledValue);
                var request = new RecordIndustrialTelemetrySampleRequest(
                    _runtimeContext.OrganizationId,
                    _runtimeContext.EnvironmentId,
                    tag.DeviceAssetId,
                    tag.TagKey,
                    nowUtc,
                    nowUtc.AddMilliseconds(_options.SampleIntervalMilliseconds),
                    1,
                    sample.Value,
                    sample.Value,
                    sample.Value,
                    sample.SourceSequence,
                    tag.SourceSystem,
                    $"{_runtimeContext.ConnectorHostId}/{tag.ConnectorId}",
                    deviceState,
                    stateOccurredAtUtc,
                    sample.Value,
                    sample.Value,
                    tag.ConnectorId);
                if (runtime.Outbox.Enqueue(request, nowUtc))
                {
                    lock (runtime.Gate)
                    {
                        runtime.DroppedCount++;
                    }

                    SignalReport(runtime.Profile.ConnectorId);
                }
            }
        }

        if (!runtime.ManifestActivated)
        {
            runtime.ManifestTracker.MarkAllEnabledActive();
            runtime.ManifestActivated = true;
        }
    }

    private async Task DeliverDueAsync(
        SimulatedConnectorRuntime runtime,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var request in runtime.Outbox.GetDue(nowUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _samplesClient.RecordSampleAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                var terminal = runtime.Outbox.MarkFailed(
                    request.SourceSequence,
                    _timeProvider.GetUtcNow());
                lock (runtime.Gate)
                {
                    runtime.ErrorCount++;
                    if (terminal)
                    {
                        runtime.DroppedCount++;
                    }
                }

                SignalReport(runtime.Profile.ConnectorId);
                continue;
            }

            runtime.Outbox.MarkDelivered(request.SourceSequence);
            lock (runtime.Gate)
            {
                runtime.ReceivedCount++;
                runtime.LastSampleAtUtc = _timeProvider.GetUtcNow();
            }

            SignalReport(runtime.Profile.ConnectorId);
        }
    }

    private ConnectorTarget CreateTarget(SimulatedConnectorRuntime runtime)
    {
        long receivedCount;
        long droppedCount;
        long errorCount;
        DateTimeOffset? lastSampleAtUtc;
        lock (runtime.Gate)
        {
            receivedCount = runtime.ReceivedCount;
            droppedCount = runtime.DroppedCount;
            errorCount = runtime.ErrorCount;
            lastSampleAtUtc = runtime.LastSampleAtUtc;
        }

        var profile = runtime.Profile;
        var tagCount = profile.Devices.Sum(device => device.Tags.Count);
        return new ConnectorTarget(
            profile.ConnectorId,
            profile.DisplayName,
            "simulated",
            "simulated-device-connector",
            "Simulated Device Connector",
            "1.0",
            profile.ConnectorId,
            profile.DisplayName,
            "running",
            errorCount == 0 ? "healthy" : "degraded",
            [
                new ConnectorCapability("runtime.status", "1.0", "runtime", ["inspect"]),
                new ConnectorCapability(
                    "industrial-telemetry.ingest",
                    "1.0",
                    "telemetry",
                    ["sample"]),
                new ConnectorCapability(
                    "device.control.command",
                    "1.0",
                    "control",
                    ["write-tag", "parameter-set", "start-stop"])
            ],
            new Dictionary<string, string>
            {
                ["adapter"] = "simulated",
                ["protocol"] = profile.Protocol,
                ["deviceCount"] = profile.Devices.Count.ToString(CultureInfo.InvariantCulture),
                ["tagCount"] = tagCount.ToString(CultureInfo.InvariantCulture)
            },
            new ConnectorCollectionHealthSnapshot(
                profile.ConnectorId,
                profile.SourceSystem,
                runtime.CounterEpoch,
                receivedCount,
                droppedCount,
                errorCount,
                lastSampleAtUtc,
                runtime.ConnectionTracker.Snapshot),
            runtime.ManifestTracker.Snapshot);
    }

    private void SignalReport(string connectorId) => _reportSignal?.Signal(connectorId);
}
