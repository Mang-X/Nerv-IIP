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
    private readonly IReadOnlyList<ConnectorRuntime> _runtimes;
    private readonly Dictionary<string, ConnectorRuntime> _runtimesById;
    private readonly SimulatedCommandReceiptStore _commandReceipts;

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
            .Select(profile => new ConnectorRuntime(
                profile,
                options,
                timeProvider,
                reportSignal,
                manifestSignal))
            .ToArray();
        _runtimesById = _runtimes.ToDictionary(
            runtime => runtime.Profile.ConnectorId,
            StringComparer.Ordinal);
        _commandReceipts = new SimulatedCommandReceiptStore(
            options.CommandReceiptCacheCapacity);
    }

    public IReadOnlyDictionary<string, int> PendingSampleCounts =>
        _runtimes.ToDictionary(
            runtime => runtime.Profile.ConnectorId,
            runtime => runtime.Outbox.Count,
            StringComparer.Ordinal);

    public int CommandReceiptCount => _commandReceipts.Count;

    public IReadOnlyList<string> CachedOperationTaskIds =>
        _commandReceipts.OperationTaskIds;

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

    public bool CanExecute(OperationTaskDispatchItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return string.Equals(task.OperationCode, "device.control.command", StringComparison.Ordinal)
            && string.Equals(task.OrganizationId, _runtimeContext.OrganizationId, StringComparison.Ordinal)
            && string.Equals(task.EnvironmentId, _runtimeContext.EnvironmentId, StringComparison.Ordinal)
            && string.Equals(task.ConnectorHostId, _runtimeContext.ConnectorHostId, StringComparison.Ordinal)
            && _runtimesById.ContainsKey(task.InstanceKey);
    }

    public Task<ConnectorOperationExecution> ExecuteAsync(
        OperationTaskDispatchItem task,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_commandReceipts.TryGet(task.OperationTaskId, out var cached))
        {
            return Task.FromResult(cached);
        }

        var execution = ExecuteCommand(task);
        return Task.FromResult(
            _commandReceipts.Store(task.OperationTaskId, execution));
    }

    private ConnectorOperationExecution ExecuteCommand(OperationTaskDispatchItem task)
    {
        var commandType = task.Parameters.GetValueOrDefault("commandType")?
            .Trim()
            .ToLowerInvariant() ?? "unknown";
        if (!_runtimesById.TryGetValue(task.InstanceKey, out var runtime))
        {
            return Failure(
                task,
                task.InstanceKey,
                "unknown",
                commandType,
                "BadNotFound",
                "Simulated connector instance was not found.");
        }

        var output = CreateOutput(task, runtime.Profile, commandType);
        if (!task.Parameters.TryGetValue("deviceAssetId", out var deviceAssetId)
            || string.IsNullOrWhiteSpace(deviceAssetId))
        {
            return Failure(
                output,
                "BadNotFound",
                "Simulated device identity was not supplied.");
        }

        var device = runtime.Profile.Devices.SingleOrDefault(candidate =>
            string.Equals(
                candidate.DeviceAssetId,
                deviceAssetId.Trim(),
                StringComparison.Ordinal));
        if (device is null)
        {
            return Failure(
                output,
                "BadNotFound",
                $"Simulated device '{deviceAssetId}' was not found.");
        }

        return commandType switch
        {
            "write-tag" => ExecuteSingleWrite(
                task,
                runtime,
                device,
                output),
            "parameter-set" => ExecuteParameterSet(
                task,
                runtime,
                device,
                output),
            "start-stop" => ExecuteStartStop(
                task,
                runtime,
                device,
                output),
            _ => Failure(
                output,
                "BadNotSupported",
                $"Simulated command type '{commandType}' is not supported.")
        };
    }

    private ConnectorOperationExecution ExecuteSingleWrite(
        OperationTaskDispatchItem task,
        ConnectorRuntime runtime,
        SimulatedDeviceProfile device,
        Dictionary<string, string> output)
    {
        if (!task.Parameters.TryGetValue("tagKey", out var tagKey)
            || string.IsNullOrWhiteSpace(tagKey))
        {
            return Failure(output, "BadNotFound", "Simulated tag identity was not supplied.");
        }

        var tag = device.Tags.SingleOrDefault(candidate =>
            string.Equals(
                candidate.TagKey,
                tagKey.Trim(),
                StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return Failure(
                output,
                "BadNotFound",
                $"Simulated tag '{device.DeviceAssetId}/{tagKey}' was not found.");
        }

        if (!task.Parameters.TryGetValue("value", out var rawValue))
        {
            return Failure(
                output,
                "BadOutOfRange",
                "Simulated tag value was not supplied.");
        }

        var validation = ValidateWrite(tag, rawValue);
        if (!validation.Succeeded)
        {
            AddReceipt(
                output,
                0,
                tag.TagKey,
                rawValue,
                validation.ReceiptCode,
                validation.Message);
            return Failure(output, validation.ReceiptCode, validation.Message);
        }

        lock (runtime.Gate)
        {
            runtime.ControlledValues[(device.DeviceAssetId, tag.TagKey)] =
                validation.Value;
        }

        AddReceipt(
            output,
            0,
            tag.TagKey,
            Format(validation.Value),
            "Good",
            "Simulated command applied.");
        output["writeCount"] = "1";
        output["successfulWriteCount"] = "1";
        SignalReport(runtime.Profile.ConnectorId);
        return ConnectorOperationExecution.Success(output);
    }

    private ConnectorOperationExecution ExecuteParameterSet(
        OperationTaskDispatchItem task,
        ConnectorRuntime runtime,
        SimulatedDeviceProfile device,
        Dictionary<string, string> output)
    {
        var parameters = task.Parameters
            .Where(item => item.Key.StartsWith("parameter.", StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (parameters.Length == 0)
        {
            return Failure(
                output,
                "BadNotSupported",
                "Simulated parameter-set contains no values.");
        }

        var writes = new List<(SimulatedTagProfile Tag, decimal Value)>();
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var tagKey = parameter.Key["parameter.".Length..];
            var tag = device.Tags.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.TagKey,
                    tagKey,
                    StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                AddReceipt(
                    output,
                    index,
                    tagKey,
                    parameter.Value,
                    "BadNotFound",
                    $"Simulated tag '{device.DeviceAssetId}/{tagKey}' was not found.");
                return Failure(
                    output,
                    "BadNotFound",
                    $"Simulated tag '{device.DeviceAssetId}/{tagKey}' was not found.");
            }

            var validation = ValidateWrite(tag, parameter.Value);
            AddReceipt(
                output,
                index,
                tag.TagKey,
                validation.Succeeded ? Format(validation.Value) : parameter.Value,
                validation.ReceiptCode,
                validation.Message);
            if (!validation.Succeeded)
            {
                return Failure(
                    output,
                    validation.ReceiptCode,
                    validation.Message);
            }

            writes.Add((tag, validation.Value));
        }

        lock (runtime.Gate)
        {
            foreach (var write in writes)
            {
                runtime.ControlledValues[
                    (device.DeviceAssetId, write.Tag.TagKey)] = write.Value;
            }
        }

        output["writeCount"] = writes.Count.ToString(CultureInfo.InvariantCulture);
        output["successfulWriteCount"] = writes.Count.ToString(
            CultureInfo.InvariantCulture);
        SetDeviceReceipt(
            output,
            "Good",
            "Simulated parameter set applied.");
        SignalReport(runtime.Profile.ConnectorId);
        return ConnectorOperationExecution.Success(output);
    }

    private ConnectorOperationExecution ExecuteStartStop(
        OperationTaskDispatchItem task,
        ConnectorRuntime runtime,
        SimulatedDeviceProfile device,
        Dictionary<string, string> output)
    {
        var value = task.Parameters.GetValueOrDefault("value")?
            .Trim()
            .ToLowerInvariant();
        var state = value switch
        {
            "start" => "running",
            "stop" => "stopped",
            _ => null
        };
        if (state is null)
        {
            return Failure(
                output,
                "BadNotSupported",
                $"Simulated start-stop value '{value ?? "<missing>"}' is not supported.");
        }

        lock (runtime.Gate)
        {
            runtime.DeviceStates[device.DeviceAssetId] = new DeviceRuntimeState(
                state,
                _timeProvider.GetUtcNow());
        }

        AddReceipt(
            output,
            0,
            "device-state",
            value!,
            "Good",
            $"Simulated device entered {state} state.");
        output["writeCount"] = "1";
        output["successfulWriteCount"] = "1";
        SignalReport(runtime.Profile.ConnectorId);
        return ConnectorOperationExecution.Success(output);
    }

    private static WriteValidation ValidateWrite(
        SimulatedTagProfile tag,
        string rawValue)
    {
        if (!tag.Writable)
        {
            return WriteValidation.Failed(
                "BadNotSupported",
                $"Simulated tag '{tag.DeviceAssetId}/{tag.TagKey}' is read-only.");
        }

        if (!decimal.TryParse(
                rawValue,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value)
            || value < tag.WritableMinimum
            || value > tag.WritableMaximum)
        {
            return WriteValidation.Failed(
                "BadOutOfRange",
                $"Simulated value for '{tag.DeviceAssetId}/{tag.TagKey}' is outside the configured range.");
        }

        return WriteValidation.Success(value);
    }

    private static Dictionary<string, string> CreateOutput(
        OperationTaskDispatchItem task,
        SimulatedConnectorProfile profile,
        string commandType) =>
        new(StringComparer.Ordinal)
        {
            ["connectorId"] = profile.ConnectorId,
            ["protocol"] = profile.Protocol,
            ["commandType"] = commandType,
            ["operationTaskId"] = task.OperationTaskId,
            ["correlationId"] = task.CorrelationId
        };

    private static ConnectorOperationExecution Failure(
        OperationTaskDispatchItem task,
        string connectorId,
        string protocol,
        string commandType,
        string receiptCode,
        string message)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["connectorId"] = connectorId,
            ["protocol"] = protocol,
            ["commandType"] = commandType,
            ["operationTaskId"] = task.OperationTaskId,
            ["correlationId"] = task.CorrelationId
        };
        return Failure(output, receiptCode, message);
    }

    private static ConnectorOperationExecution Failure(
        Dictionary<string, string> output,
        string receiptCode,
        string message)
    {
        SetDeviceReceipt(output, receiptCode, message);
        return ConnectorOperationExecution.Failed(
            "simulated.command.rejected",
            message,
            "validation",
            false,
            output);
    }

    private static void AddReceipt(
        IDictionary<string, string> output,
        int index,
        string tagKey,
        string writtenValue,
        string receiptCode,
        string message)
    {
        var prefix = $"receipt.{index.ToString(CultureInfo.InvariantCulture)}";
        output[$"{prefix}.status"] = receiptCode == "Good" ? "Good" : "Bad";
        output[$"{prefix}.code"] = receiptCode;
        output[$"{prefix}.message"] = message;
        output[$"{prefix}.tagKey"] = tagKey;
        output[$"{prefix}.writtenValue"] = writtenValue;
        SetDeviceReceipt(output, receiptCode, message);
    }

    private static void SetDeviceReceipt(
        IDictionary<string, string> output,
        string receiptCode,
        string message)
    {
        output["deviceReceiptCode"] = receiptCode;
        output["deviceReceiptMessage"] = message;
    }

    private static string Format(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

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
        ConnectorRuntime runtime,
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
        ConnectorRuntime runtime,
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

    private ConnectorTarget CreateTarget(ConnectorRuntime runtime)
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

    private sealed class ConnectorRuntime
    {
        public ConnectorRuntime(
            SimulatedConnectorProfile profile,
            SimulatedConnectorOptions options,
            TimeProvider timeProvider,
            IConnectorReportSignal? reportSignal,
            IConnectorManifestSignal? manifestSignal)
        {
            Profile = profile;
            Outbox = new SimulatedSampleOutbox(
                options.MaxPendingSamples,
                options.MaxDeliveryAttempts,
                TimeSpan.FromMilliseconds(options.RetryBaseMilliseconds));
            ConnectionTracker = new ConnectorConnectionStateTracker(
                profile.ConnectorId,
                timeProvider,
                reportSignal is null ? static _ => { } : reportSignal.Signal);
            ManifestTracker = new ConnectorTagManifestTracker(
                profile.ConnectorId,
                profile.SourceSystem,
                profile.Devices
                    .SelectMany(device => device.Tags)
                    .Select(tag => new ConnectorTagManifestDefinition(
                        tag.DeviceAssetId,
                        tag.TagKey,
                        true,
                        tag.ProtocolAddress))
                    .ToArray(),
                timeProvider,
                manifestSignal is null ? static _ => { } : manifestSignal.Signal);
            var observedAtUtc = timeProvider.GetUtcNow();
            DeviceStates = profile.Devices.ToDictionary(
                device => device.DeviceAssetId,
                _ => new DeviceRuntimeState("running", observedAtUtc),
                StringComparer.Ordinal);
            ConnectionTracker.MarkAlive();
        }

        public object Gate { get; } = new();
        public SimulatedConnectorProfile Profile { get; }
        public SimulatedSampleOutbox Outbox { get; }
        public ConnectorConnectionStateTracker ConnectionTracker { get; }
        public ConnectorTagManifestTracker ManifestTracker { get; }
        public Guid CounterEpoch { get; } = Guid.CreateVersion7();
        public Dictionary<(string DeviceAssetId, string TagKey), long> LastGeneratedCycles { get; } = [];
        public Dictionary<(string DeviceAssetId, string TagKey), decimal?> ControlledValues { get; } = [];
        public Dictionary<string, DeviceRuntimeState> DeviceStates { get; }
        public bool ManifestActivated { get; set; }
        public long ReceivedCount { get; set; }
        public long DroppedCount { get; set; }
        public long ErrorCount { get; set; }
        public DateTimeOffset? LastSampleAtUtc { get; set; }
    }

    private sealed record DeviceRuntimeState(
        string State,
        DateTimeOffset OccurredAtUtc);

    private sealed record WriteValidation(
        bool Succeeded,
        decimal Value,
        string ReceiptCode,
        string Message)
    {
        public static WriteValidation Success(decimal value) =>
            new(true, value, "Good", "Simulated command applied.");

        public static WriteValidation Failed(string receiptCode, string message) =>
            new(false, 0m, receiptCode, message);
    }
}
