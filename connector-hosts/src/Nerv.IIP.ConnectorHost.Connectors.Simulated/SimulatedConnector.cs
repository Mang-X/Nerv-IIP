using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
            options,
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
        var cycleAtUtc = ResolveCycleTimestamp(cycle);
        foreach (var runtime in _runtimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerateCycle(runtime, cycleAtUtc, cycle);
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

    private DateTimeOffset ResolveCycleTimestamp(long cycle) =>
        _options.EpochUtc.AddTicks(
            checked(
                cycle
                * TimeSpan.FromMilliseconds(_options.SampleIntervalMilliseconds).Ticks));

    private void GenerateCycle(
        SimulatedConnectorRuntime runtime,
        DateTimeOffset nowUtc,
        long cycle)
    {
        foreach (var device in runtime.Profile.Devices)
        {
            var stateObservationTagKey = device.Tags
                .Select(tag => tag.TagKey)
                .Min(StringComparer.Ordinal);
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
                lock (runtime.Gate)
                {
                    runtime.ControlledValues.TryGetValue(identity, out controlledValue);
                }

                var sample = _evaluator.Evaluate(tag, nowUtc, cycle, controlledValue);
                RecordIndustrialTelemetrySampleRequest request;
                if (string.Equals(
                    tag.TagKey,
                    stateObservationTagKey,
                    StringComparison.Ordinal))
                {
                    lock (runtime.Gate)
                    {
                        var state = runtime.DeviceStates[device.DeviceAssetId];
                        var pending = state.PendingObservations.First;
                        if (pending is not null
                            && pending.Value.SourceSequence is null)
                        {
                            var observation = pending.Value with
                            {
                                OccurredAtUtc = pending.Value.OccurredAtUtc ?? nowUtc
                            };
                            request = CreateRequest(
                                tag,
                                sample,
                                nowUtc,
                                observation.State,
                                observation.OccurredAtUtc);
                            pending.Value = observation with
                            {
                                SourceSequence = request.SourceSequence
                            };
                        }
                        else
                        {
                            request = CreateRequest(tag, sample, nowUtc);
                        }
                    }
                }
                else
                {
                    request = CreateRequest(tag, sample, nowUtc);
                }

                var evicted = runtime.Outbox.Enqueue(request, nowUtc);
                if (evicted is not null)
                {
                    lock (runtime.Gate)
                    {
                        ReleaseStateObservation(runtime, evicted);
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

    private RecordIndustrialTelemetrySampleRequest CreateRequest(
        SimulatedTagProfile tag,
        SimulatedScenarioSample sample,
        DateTimeOffset bucketStartUtc,
        string? deviceState = null,
        DateTimeOffset? stateOccurredAtUtc = null)
    {
        var request = new RecordIndustrialTelemetrySampleRequest(
            _runtimeContext.OrganizationId,
            _runtimeContext.EnvironmentId,
            tag.DeviceAssetId,
            tag.TagKey,
            bucketStartUtc,
            bucketStartUtc.AddMilliseconds(_options.SampleIntervalMilliseconds),
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
        return request with
        {
            SourceSequence = ContentAddressedSourceSequence(
                sample.SourceSequence,
                request)
        };
    }

    private static string ContentAddressedSourceSequence(
        string cycleSourceSequence,
        RecordIndustrialTelemetrySampleRequest request)
    {
        var canonical = string.Join(
            '\u001f',
            [
                cycleSourceSequence,
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.TagKey,
                request.BucketStartUtc.ToString("O", CultureInfo.InvariantCulture),
                request.BucketEndUtc.ToString("O", CultureInfo.InvariantCulture),
                request.SampleCount.ToString(CultureInfo.InvariantCulture),
                request.MinValue.ToString(CultureInfo.InvariantCulture),
                request.MaxValue.ToString(CultureInfo.InvariantCulture),
                request.AverageValue.ToString(CultureInfo.InvariantCulture),
                request.SourceSystem ?? string.Empty,
                request.SourceConnector ?? string.Empty,
                request.DeviceState ?? string.Empty,
                request.StateOccurredAtUtc?.ToString("O", CultureInfo.InvariantCulture)
                    ?? string.Empty,
                request.FirstValue?.ToString(CultureInfo.InvariantCulture)
                    ?? string.Empty,
                request.LastValue?.ToString(CultureInfo.InvariantCulture)
                    ?? string.Empty,
                request.CollectionConnectorId ?? string.Empty
            ]);
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"{cycleSourceSequence}:{digest}";
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                        ReleaseStateObservation(runtime, request);
                        runtime.DroppedCount++;
                    }
                }

                SignalReport(runtime.Profile.ConnectorId);
                continue;
            }

            runtime.Outbox.MarkDelivered(request.SourceSequence);
            lock (runtime.Gate)
            {
                CompleteStateObservation(runtime, request);
                runtime.ReceivedCount++;
                runtime.LastSampleAtUtc = _timeProvider.GetUtcNow();
            }

            SignalReport(runtime.Profile.ConnectorId);
        }
    }

    private static void CompleteStateObservation(
        SimulatedConnectorRuntime runtime,
        RecordIndustrialTelemetrySampleRequest request)
    {
        var state = runtime.DeviceStates[request.DeviceAssetId];
        if (state.PendingObservations.First is { } pending
            && string.Equals(
                pending.Value.SourceSequence,
                request.SourceSequence,
                StringComparison.Ordinal))
        {
            state.PendingObservations.RemoveFirst();
        }
    }

    private static void ReleaseStateObservation(
        SimulatedConnectorRuntime runtime,
        RecordIndustrialTelemetrySampleRequest request)
    {
        var state = runtime.DeviceStates[request.DeviceAssetId];
        if (state.PendingObservations.First is { } pending
            && string.Equals(
                pending.Value.SourceSequence,
                request.SourceSequence,
                StringComparison.Ordinal))
        {
            pending.Value = pending.Value with
            {
                SourceSequence = null
            };
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
