using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

public sealed class OpcUaDeviceControlOperationExecutor(
    OpcUaConnectorOptions options,
    IOpcUaClient opcUaClient) : IConnectorOperationExecutor
{
    private readonly Dictionary<(string DeviceAssetId, string TagKey), OpcUaTagSubscription> tags = options.Tags
        .ToDictionary(x => (x.DeviceAssetId, x.TagKey.Trim().ToLowerInvariant()));

    public bool CanExecute(OperationTaskDispatchItem task)
    {
        return string.Equals(task.OperationCode, "device.control.command", StringComparison.Ordinal)
            && string.Equals(task.ConnectorHostId, options.ConnectorHostId, StringComparison.Ordinal)
            && string.Equals(task.OrganizationId, options.OrganizationId, StringComparison.Ordinal)
            && string.Equals(task.EnvironmentId, options.EnvironmentId, StringComparison.Ordinal);
    }

    public async Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
    {
        if (!task.Parameters.TryGetValue("commandType", out var commandType))
        {
            return ValidationFailure(task, "opcua.command_type.missing", "Device control command type is required.");
        }

        var writes = ResolveWrites(task, commandType);
        if (!writes.Succeeded)
        {
            return writes.Failure!;
        }

        var output = new Dictionary<string, string>
        {
            ["connectorId"] = options.ConnectorId,
            ["protocol"] = "opcua",
            ["commandType"] = commandType
        };
        await opcUaClient.ConnectAsync(new OpcUaConnectionOptions(
            options.EndpointUrl,
            options.SecurityPolicy,
            options.SecurityMode,
            options.CredentialReference,
            options.AutoAcceptUntrustedServerCertificates), cancellationToken);
        try
        {
            var successfulWriteCount = 0;
            for (var index = 0; index < writes.Writes.Count; index++)
            {
                var write = writes.Writes[index];
                var receipt = await opcUaClient.WriteAsync(new OpcUaWriteRequest(write.NodeId, write.Value), cancellationToken);
                AddReceiptOutput(output, index, write, receipt);
                if (!string.Equals(receipt.Status, "Good", StringComparison.OrdinalIgnoreCase))
                {
                    output["failedWriteIndex"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    output["successfulWriteCount"] = successfulWriteCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    output["writeCount"] = writes.Writes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return ConnectorOperationExecution.Failed("opcua.write.rejected", receipt.Message, "runtime", false, output);
                }

                successfulWriteCount++;
            }

            output["writeCount"] = writes.Writes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            output["successfulWriteCount"] = successfulWriteCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ConnectorOperationExecution.Success(output);
        }
        finally
        {
            await opcUaClient.DisconnectAsync(cancellationToken);
        }
    }

    private WritePlan ResolveWrites(OperationTaskDispatchItem task, string commandType)
    {
        var normalizedCommandType = commandType.Trim().ToLowerInvariant();
        if (normalizedCommandType is "write-tag" or "start-stop")
        {
            if (!task.Parameters.TryGetValue("deviceAssetId", out var deviceAssetId)
                || !task.Parameters.TryGetValue("tagKey", out var tagKey)
                || !task.Parameters.TryGetValue("value", out var value))
            {
                return WritePlan.Fail(ValidationFailure(task, "opcua.write.parameters_missing", "Device control write requires deviceAssetId, tagKey and value."));
            }

            var tag = FindTag(deviceAssetId, tagKey);
            if (tag is null)
            {
                return WritePlan.Fail(ValidationFailure(task, "opcua.tag.not_mapped", $"OPC UA tag mapping was not found: {deviceAssetId}/{tagKey}."));
            }

            return WritePlan.Success([new OpcUaPlannedWrite(tag.NodeId, value)]);
        }

        if (normalizedCommandType == "parameter-set")
        {
            if (!task.Parameters.TryGetValue("deviceAssetId", out var deviceAssetId))
            {
                return WritePlan.Fail(ValidationFailure(task, "opcua.device.missing", "Parameter-set command requires deviceAssetId."));
            }

            var writes = new List<OpcUaPlannedWrite>();
            foreach (var item in task.Parameters.Where(x => x.Key.StartsWith("parameter.", StringComparison.Ordinal)).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var tagKey = item.Key["parameter.".Length..];
                var tag = FindTag(deviceAssetId, tagKey);
                if (tag is null)
                {
                    return WritePlan.Fail(ValidationFailure(task, "opcua.tag.not_mapped", $"OPC UA tag mapping was not found: {deviceAssetId}/{tagKey}."));
                }

                writes.Add(new OpcUaPlannedWrite(tag.NodeId, item.Value));
            }

            return writes.Count == 0
                ? WritePlan.Fail(ValidationFailure(task, "opcua.parameter_set.empty", "Parameter-set command has no tag values."))
                : WritePlan.Success(writes);
        }

        return WritePlan.Fail(ValidationFailure(task, "opcua.command_type.unsupported", $"Unsupported device control command type: {commandType}."));
    }

    private OpcUaTagSubscription? FindTag(string deviceAssetId, string tagKey)
    {
        return tags.GetValueOrDefault((deviceAssetId.Trim(), tagKey.Trim().ToLowerInvariant()));
    }

    private static ConnectorOperationExecution ValidationFailure(OperationTaskDispatchItem task, string code, string message)
    {
        return ConnectorOperationExecution.Failed(code, message, "validation", false, new Dictionary<string, string>
        {
            ["operationTaskId"] = task.OperationTaskId,
            ["instanceKey"] = task.InstanceKey
        });
    }

    private static void AddReceiptOutput(
        IDictionary<string, string> output,
        int index,
        OpcUaPlannedWrite write,
        OpcUaWriteReceipt receipt)
    {
        var prefix = $"receipt.{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        output[$"{prefix}.status"] = receipt.Status;
        output[$"{prefix}.code"] = receipt.ReceiptCode;
        output[$"{prefix}.message"] = receipt.Message;
        output[$"{prefix}.nodeId"] = write.NodeId;
        output[$"{prefix}.writtenValue"] = write.Value;

        output["deviceReceiptCode"] = receipt.ReceiptCode;
        output["deviceReceiptMessage"] = receipt.Message;
        output["nodeId"] = write.NodeId;
        output["writtenValue"] = write.Value;
    }

    private sealed record OpcUaPlannedWrite(string NodeId, string Value);

    private sealed record WritePlan(bool Succeeded, IReadOnlyList<OpcUaPlannedWrite> Writes, ConnectorOperationExecution? Failure)
    {
        public static WritePlan Success(IReadOnlyList<OpcUaPlannedWrite> writes) => new(true, writes, null);
        public static WritePlan Fail(ConnectorOperationExecution failure) => new(false, [], failure);
    }
}
