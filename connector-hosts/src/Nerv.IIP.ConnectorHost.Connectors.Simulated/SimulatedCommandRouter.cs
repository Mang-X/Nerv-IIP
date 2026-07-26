using System.Globalization;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

internal sealed class SimulatedCommandRouter
{
    private readonly ConnectorHostRuntimeContext _runtimeContext;
    private readonly IReadOnlyDictionary<string, SimulatedConnectorRuntime> _runtimesById;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _epochUtc;
    private readonly long _sampleIntervalTicks;
    private readonly IConnectorReportSignal? _reportSignal;
    private readonly SimulatedCommandReceiptStore _receipts;

    public SimulatedCommandRouter(
        ConnectorHostRuntimeContext runtimeContext,
        IReadOnlyDictionary<string, SimulatedConnectorRuntime> runtimesById,
        SimulatedConnectorOptions options,
        TimeProvider timeProvider,
        IConnectorReportSignal? reportSignal,
        int receiptCapacity)
    {
        _runtimeContext = runtimeContext;
        _runtimesById = runtimesById;
        _timeProvider = timeProvider;
        _epochUtc = options.EpochUtc;
        _sampleIntervalTicks = TimeSpan.FromMilliseconds(
            options.SampleIntervalMilliseconds).Ticks;
        _reportSignal = reportSignal;
        _receipts = new SimulatedCommandReceiptStore(receiptCapacity);
    }

    public int ReceiptCount => _receipts.Count;
    public IReadOnlyList<string> CachedOperationTaskIds => _receipts.OperationTaskIds;

    public bool CanExecute(OperationTaskDispatchItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return string.Equals(task.OperationCode, "device.control.command", StringComparison.Ordinal)
            && string.Equals(task.OrganizationId, _runtimeContext.OrganizationId, StringComparison.Ordinal)
            && string.Equals(task.EnvironmentId, _runtimeContext.EnvironmentId, StringComparison.Ordinal)
            && string.Equals(task.ConnectorHostId, _runtimeContext.ConnectorHostId, StringComparison.Ordinal)
            && _runtimesById.ContainsKey(task.InstanceKey);
    }

    public ConnectorOperationExecution Execute(OperationTaskDispatchItem task)
    {
        if (_receipts.TryGet(task.OperationTaskId, out var cached))
        {
            return cached;
        }

        return _receipts.Store(task.OperationTaskId, ExecuteCommand(task));
    }

    private ConnectorOperationExecution ExecuteCommand(OperationTaskDispatchItem task)
    {
        var commandType = task.Parameters.GetValueOrDefault("commandType")?
            .Trim()
            .ToLowerInvariant() ?? "unknown";
        if (!_runtimesById.TryGetValue(task.InstanceKey, out var runtime))
        {
            return SimulatedCommandReceiptFactory.Failure(
                task,
                task.InstanceKey,
                "unknown",
                commandType,
                "BadNotFound",
                "Simulated connector instance was not found.");
        }

        var output = SimulatedCommandReceiptFactory.CreateOutput(
            task,
            runtime.Profile,
            commandType);
        if (!task.Parameters.TryGetValue("deviceAssetId", out var deviceAssetId)
            || string.IsNullOrWhiteSpace(deviceAssetId))
        {
            return SimulatedCommandReceiptFactory.Failure(
                output,
                "BadNotFound",
                "Simulated device identity was not supplied.");
        }

        var device = runtime.Profile.Devices.SingleOrDefault(candidate =>
            string.Equals(candidate.DeviceAssetId, deviceAssetId.Trim(), StringComparison.Ordinal));
        if (device is null)
        {
            return SimulatedCommandReceiptFactory.Failure(
                output,
                "BadNotFound",
                $"Simulated device '{deviceAssetId}' was not found.");
        }

        return commandType switch
        {
            "write-tag" => ExecuteSingleWrite(task, runtime, device, output),
            "parameter-set" => ExecuteParameterSet(task, runtime, device, output),
            "start-stop" => ExecuteStartStop(task, runtime, device, output),
            _ => SimulatedCommandReceiptFactory.Failure(
                output,
                "BadNotSupported",
                $"Simulated command type '{commandType}' is not supported.")
        };
    }

    private ConnectorOperationExecution ExecuteSingleWrite(
        OperationTaskDispatchItem task,
        SimulatedConnectorRuntime runtime,
        SimulatedDeviceProfile device,
        Dictionary<string, string> output)
    {
        if (!task.Parameters.TryGetValue("tagKey", out var tagKey)
            || string.IsNullOrWhiteSpace(tagKey))
        {
            return SimulatedCommandReceiptFactory.Failure(
                output,
                "BadNotFound",
                "Simulated tag identity was not supplied.");
        }

        var tag = device.Tags.SingleOrDefault(candidate =>
            string.Equals(candidate.TagKey, tagKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return SimulatedCommandReceiptFactory.Failure(
                output,
                "BadNotFound",
                $"Simulated tag '{device.DeviceAssetId}/{tagKey}' was not found.");
        }

        if (!task.Parameters.TryGetValue("value", out var rawValue))
        {
            return SimulatedCommandReceiptFactory.Failure(
                output,
                "BadOutOfRange",
                "Simulated tag value was not supplied.");
        }

        var validation = ValidateWrite(tag, rawValue);
        if (!validation.Succeeded)
        {
            SimulatedCommandReceiptFactory.AddReceipt(
                output,
                0,
                tag.TagKey,
                rawValue,
                validation.ReceiptCode,
                validation.Message);
            return SimulatedCommandReceiptFactory.Failure(
                output,
                validation.ReceiptCode,
                validation.Message);
        }

        lock (runtime.Gate)
        {
            runtime.ControlledValues[(device.DeviceAssetId, tag.TagKey)] = validation.Value;
        }

        SimulatedCommandReceiptFactory.AddReceipt(
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
        SimulatedConnectorRuntime runtime,
        SimulatedDeviceProfile device,
        Dictionary<string, string> output)
    {
        var parameters = task.Parameters
            .Where(item => item.Key.StartsWith("parameter.", StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (parameters.Length == 0)
        {
            return SimulatedCommandReceiptFactory.Failure(
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
                string.Equals(candidate.TagKey, tagKey, StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                SimulatedCommandReceiptFactory.AddReceipt(
                    output,
                    index,
                    tagKey,
                    parameter.Value,
                    "BadNotFound",
                    $"Simulated tag '{device.DeviceAssetId}/{tagKey}' was not found.");
                return SimulatedCommandReceiptFactory.Failure(
                    output,
                    "BadNotFound",
                    $"Simulated tag '{device.DeviceAssetId}/{tagKey}' was not found.");
            }

            var validation = ValidateWrite(tag, parameter.Value);
            SimulatedCommandReceiptFactory.AddReceipt(
                output,
                index,
                tag.TagKey,
                validation.Succeeded ? Format(validation.Value) : parameter.Value,
                validation.ReceiptCode,
                validation.Message);
            if (!validation.Succeeded)
            {
                return SimulatedCommandReceiptFactory.Failure(
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
                runtime.ControlledValues[(device.DeviceAssetId, write.Tag.TagKey)] = write.Value;
            }
        }

        output["writeCount"] = writes.Count.ToString(CultureInfo.InvariantCulture);
        output["successfulWriteCount"] = writes.Count.ToString(CultureInfo.InvariantCulture);
        SimulatedCommandReceiptFactory.SetDeviceReceipt(
            output,
            "Good",
            "Simulated parameter set applied.");
        SignalReport(runtime.Profile.ConnectorId);
        return ConnectorOperationExecution.Success(output);
    }

    private ConnectorOperationExecution ExecuteStartStop(
        OperationTaskDispatchItem task,
        SimulatedConnectorRuntime runtime,
        SimulatedDeviceProfile device,
        Dictionary<string, string> output)
    {
        var value = task.Parameters.GetValueOrDefault("value")?.Trim().ToLowerInvariant();
        var state = value switch
        {
            "start" => "running",
            "stop" => "stopped",
            _ => null
        };
        if (state is null)
        {
            return SimulatedCommandReceiptFactory.Failure(
                output,
                "BadNotSupported",
                $"Simulated start-stop value '{value ?? "<missing>"}' is not supported.");
        }

        lock (runtime.Gate)
        {
            var current = runtime.DeviceStates[device.DeviceAssetId];
            if (!string.Equals(current.State, state, StringComparison.Ordinal))
            {
                runtime.DeviceStates[device.DeviceAssetId] = new SimulatedDeviceRuntimeState(
                    state,
                    new SimulatedPendingDeviceStateObservation(
                        state,
                        null,
                        ResolveCycleTimestamp(_timeProvider.GetUtcNow())));
            }
        }

        SimulatedCommandReceiptFactory.AddReceipt(
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

    private DateTimeOffset ResolveCycleTimestamp(DateTimeOffset observedAtUtc)
    {
        var elapsed = observedAtUtc - _epochUtc;
        var cycle = elapsed <= TimeSpan.Zero
            ? 0
            : elapsed.Ticks / _sampleIntervalTicks;
        return _epochUtc.AddTicks(checked(cycle * _sampleIntervalTicks));
    }

    private static SimulatedWriteValidation ValidateWrite(
        SimulatedTagProfile tag,
        string rawValue)
    {
        if (!tag.Writable)
        {
            return SimulatedWriteValidation.Failed(
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
            return SimulatedWriteValidation.Failed(
                "BadOutOfRange",
                $"Simulated value for '{tag.DeviceAssetId}/{tag.TagKey}' is outside the configured range.");
        }

        return SimulatedWriteValidation.Success(value);
    }

    private void SignalReport(string connectorId) => _reportSignal?.Signal(connectorId);

    private static string Format(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private sealed record SimulatedWriteValidation(
        bool Succeeded,
        decimal Value,
        string ReceiptCode,
        string Message)
    {
        public static SimulatedWriteValidation Success(decimal value) =>
            new(true, value, "Good", "Simulated command applied.");

        public static SimulatedWriteValidation Failed(string receiptCode, string message) =>
            new(false, 0m, receiptCode, message);
    }
}

internal static class SimulatedCommandReceiptFactory
{
    public static Dictionary<string, string> CreateOutput(
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

    public static ConnectorOperationExecution Failure(
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

    public static ConnectorOperationExecution Failure(
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

    public static void AddReceipt(
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

    public static void SetDeviceReceipt(
        IDictionary<string, string> output,
        string receiptCode,
        string message)
    {
        output["deviceReceiptCode"] = receiptCode;
        output["deviceReceiptMessage"] = message;
    }
}
