using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests;

public sealed class OpcUaDeviceControlOperationTests
{
    [Fact]
    public async Task OpcUa_executor_writes_tag_value_and_returns_device_receipt_output()
    {
        var opcUa = new RecordingOpcUaClient();
        var executor = new OpcUaDeviceControlOperationExecutor(CreateOptions(), opcUa);
        var task = CreateTask(new Dictionary<string, string>
        {
            ["commandType"] = "write-tag",
            ["deviceAssetId"] = "DEV-CNC-01",
            ["tagKey"] = "spindle.speed",
            ["value"] = "80"
        });

        var result = await executor.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.Succeeded);
        var write = Assert.Single(opcUa.Writes);
        Assert.Equal("ns=2;s=Line1.SpindleSpeed", write.NodeId);
        Assert.Equal("80", write.Value);
        Assert.Equal("opcua.write.accepted", result.Output["deviceReceiptCode"]);
        Assert.Equal("ns=2;s=Line1.SpindleSpeed", result.Output["nodeId"]);
        Assert.Equal("80", result.Output["writtenValue"]);
    }

    [Fact]
    public async Task OpcUa_executor_returns_validation_failure_for_unmapped_tag()
    {
        var executor = new OpcUaDeviceControlOperationExecutor(CreateOptions(), new RecordingOpcUaClient());
        var task = CreateTask(new Dictionary<string, string>
        {
            ["commandType"] = "write-tag",
            ["deviceAssetId"] = "DEV-CNC-01",
            ["tagKey"] = "unknown",
            ["value"] = "80"
        });

        var result = await executor.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("opcua.tag.not_mapped", result.FailureCode);
        Assert.Equal("validation", result.FailureCategory);
        Assert.False(result.Retryable);
    }

    [Fact]
    public async Task OpcUa_executor_returns_runtime_failure_with_device_receipt_when_write_is_rejected()
    {
        var opcUa = new RecordingOpcUaClient(new OpcUaWriteReceipt("BadOutOfRange", "BadOutOfRange", "Value is outside PLC range."));
        var executor = new OpcUaDeviceControlOperationExecutor(CreateOptions(), opcUa);
        var task = CreateTask(new Dictionary<string, string>
        {
            ["commandType"] = "write-tag",
            ["deviceAssetId"] = "DEV-CNC-01",
            ["tagKey"] = "spindle.speed",
            ["value"] = "999"
        });

        var result = await executor.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("opcua.write.rejected", result.FailureCode);
        Assert.Equal("runtime", result.FailureCategory);
        Assert.False(result.Retryable);
        Assert.Equal("BadOutOfRange", result.Output["deviceReceiptCode"]);
        Assert.Equal("999", result.Output["writtenValue"]);
    }

    private static OpcUaConnectorOptions CreateOptions()
    {
        return new OpcUaConnectorOptions(
            "opcua-line1",
            "connector-host-001",
            "org-001",
            "env-dev",
            "opc.tcp://localhost:4840",
            "None",
            "None",
            null,
            "ns=0;i=85",
            [
                new OpcUaTagSubscription("DEV-CNC-01", "spindle.speed", "ns=2;s=Line1.SpindleSpeed", 1000, 60)
            ]);
    }

    private static OperationTaskDispatchItem CreateTask(IReadOnlyDictionary<string, string> parameters)
    {
        return new OperationTaskDispatchItem(
            "op-000001",
            "attempt-000001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "opcua-line1",
            "device.control.command",
            "corr-device-control",
            parameters,
            "lease-000001",
            DateTimeOffset.Parse("2026-07-07T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-07T00:05:00Z"),
            1,
            300,
            1);
    }

    private sealed class RecordingOpcUaClient(OpcUaWriteReceipt? receipt = null) : IOpcUaClient
    {
        public List<OpcUaWriteRequest> Writes { get; } = [];

        public Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OpcUaNode>>([]);
        }

        public Task SubscribeAsync(IReadOnlyList<OpcUaTagSubscription> tags, Func<OpcUaDataChange, CancellationToken, Task> onDataChange, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<OpcUaWriteReceipt> WriteAsync(OpcUaWriteRequest request, CancellationToken cancellationToken)
        {
            Writes.Add(request);
            return Task.FromResult(receipt ?? new OpcUaWriteReceipt("Good", "opcua.write.accepted", "Write accepted by simulated device."));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
