using System.Collections;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.TestUtilities;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests;

public sealed class SimulatedCommandTests
{
    [Fact]
    public async Task Start_stop_success_changes_only_the_addressed_device()
    {
        var fixture = CreateFixture();
        var task = CreateTask(
            "op-stop-1",
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "start-stop",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["value"] = "stop"
            });

        var result = await fixture.Connector.ExecuteAsync(task, CancellationToken.None);
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        AssertReceiptContext(result, task, "CONN-OPCUA-01", "opcua", "start-stop", "Good");
        var stateObservations = fixture.Samples.Requests
            .Where(request => request.DeviceState is not null)
            .ToArray();
        Assert.Equal(46, stateObservations.Length);
        Assert.Single(
            stateObservations,
            request => request.DeviceAssetId == "DEV-CNC-01"
                && request.DeviceState == "stopped");
        Assert.All(
            stateObservations.Where(request => request.DeviceAssetId != "DEV-CNC-01"),
            request => Assert.Equal("running", request.DeviceState));
    }

    [Fact]
    public async Task Write_tag_success_is_observable_only_on_the_addressed_point()
    {
        var fixture = CreateFixture();
        var task = CreateTask(
            "op-write-1",
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "write-tag",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["tagKey"] = "spindle-speed",
                ["value"] = "2500"
            });

        var result = await fixture.Connector.ExecuteAsync(task, CancellationToken.None);
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            2500m,
            fixture.Samples.Requests.Single(request =>
                request.DeviceAssetId == "DEV-CNC-01"
                && request.TagKey == "spindle-speed").AverageValue);
        Assert.NotEqual(
            2500m,
            fixture.Samples.Requests.Single(request =>
                request.DeviceAssetId == "DEV-CNC-02"
                && request.TagKey == "spindle-speed").AverageValue);
    }

    [Fact]
    public async Task Parameter_set_preserves_stable_indexed_receipts()
    {
        var fixture = CreateFixture();
        var task = CreateTask(
            "op-parameters-1",
            "CONN-MODBUS-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "parameter-set",
                ["deviceAssetId"] = "DEV-CTG-01",
                ["parameter.bath-temperature"] = "30",
                ["parameter.bath-ph"] = "6.4"
            });

        var result = await fixture.Connector.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("2", result.Output["writeCount"]);
        Assert.Equal("bath-ph", result.Output["receipt.0.tagKey"]);
        Assert.Equal("6.4", result.Output["receipt.0.writtenValue"]);
        Assert.Equal("Good", result.Output["receipt.0.code"]);
        Assert.Equal("bath-temperature", result.Output["receipt.1.tagKey"]);
        Assert.Equal("30", result.Output["receipt.1.writtenValue"]);
        Assert.Equal("Good", result.Output["receipt.1.code"]);
    }

    [Fact]
    public async Task Duplicate_operation_task_returns_the_same_immutable_result_without_reapplying_state()
    {
        var fixture = CreateFixture();
        var task = CreateTask(
            "op-idempotent-1",
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "start-stop",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["value"] = "stop"
            });

        var first = await fixture.Connector.ExecuteAsync(task, CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var duplicate = await fixture.Connector.ExecuteAsync(task, CancellationToken.None);
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(first, duplicate);
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary)duplicate.Output).Add("mutated", "not-allowed"));
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            fixture.Samples.Requests.Single(request =>
                request.DeviceAssetId == "DEV-CNC-01"
                && request.DeviceState == "stopped").StateOccurredAtUtc);
    }

    [Fact]
    public async Task Start_stop_emits_state_only_for_an_actual_transition_on_the_addressed_device()
    {
        var fixture = CreateFixture();
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);
        fixture.Samples.Requests.Clear();

        await fixture.Connector.ExecuteAsync(
            StartTask("op-start-no-change", "DEV-CNC-01", "start"),
            CancellationToken.None);
        await fixture.Connector.ExecuteAsync(
            StartTask("op-stop-transition", "DEV-CNC-01", "stop"),
            CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        var transition = Assert.Single(
            fixture.Samples.Requests,
            request => request.DeviceState is not null);
        Assert.Equal("DEV-CNC-01", transition.DeviceAssetId);
        Assert.Equal("stopped", transition.DeviceState);
    }

    [Fact]
    public async Task Same_cycle_restart_after_write_tag_does_not_reuse_the_controlled_payload_source_sequence()
    {
        var controlled = CreateFixture(
            timestamp: DateTimeOffset.Parse("2026-07-26T00:00:00.100Z"));
        var result = await controlled.Connector.ExecuteAsync(
            CreateTask(
                "op-write-before-restart",
                "CONN-OPCUA-01",
                new Dictionary<string, string>
                {
                    ["commandType"] = "write-tag",
                    ["deviceAssetId"] = "DEV-CNC-01",
                    ["tagKey"] = "spindle-speed",
                    ["value"] = "2500"
                }),
            CancellationToken.None);
        await controlled.Connector.RunCollectionCycleAsync(CancellationToken.None);
        var controlledRequest = controlled.Samples.Requests.Single(request =>
            request.DeviceAssetId == "DEV-CNC-01"
            && request.TagKey == "spindle-speed");

        var restarted = CreateFixture(
            timestamp: DateTimeOffset.Parse("2026-07-26T00:00:01.900Z"));
        await restarted.Connector.RunCollectionCycleAsync(CancellationToken.None);
        var restartedRequest = restarted.Samples.Requests.Single(request =>
            request.DeviceAssetId == "DEV-CNC-01"
            && request.TagKey == "spindle-speed");

        Assert.True(result.Succeeded);
        Assert.Equal(2500m, controlledRequest.AverageValue);
        Assert.NotEqual(controlledRequest.AverageValue, restartedRequest.AverageValue);
        Assert.NotEqual(controlledRequest.SourceSequence, restartedRequest.SourceSequence);
    }

    [Fact]
    public async Task Same_cycle_restart_after_start_stop_does_not_reuse_the_state_payload_source_sequence()
    {
        var controlled = CreateFixture(
            timestamp: DateTimeOffset.Parse("2026-07-26T00:00:00.100Z"));
        var result = await controlled.Connector.ExecuteAsync(
            StartTask("op-stop-before-restart", "DEV-CNC-01", "stop"),
            CancellationToken.None);
        await controlled.Connector.RunCollectionCycleAsync(CancellationToken.None);
        var stoppedRequest = controlled.Samples.Requests.Single(request =>
            request.DeviceAssetId == "DEV-CNC-01"
            && request.DeviceState == "stopped");

        var restarted = CreateFixture(
            timestamp: DateTimeOffset.Parse("2026-07-26T00:00:01.900Z"));
        await restarted.Connector.RunCollectionCycleAsync(CancellationToken.None);
        var runningRequest = restarted.Samples.Requests.Single(request =>
            request.DeviceAssetId == "DEV-CNC-01"
            && request.DeviceState == "running");

        Assert.True(result.Succeeded);
        Assert.NotEqual(stoppedRequest, runningRequest);
        Assert.NotEqual(stoppedRequest.SourceSequence, runningRequest.SourceSequence);
    }

    [Fact]
    public async Task Stop_then_start_before_collection_emits_both_transitions_in_fifo_order()
    {
        var fixture = CreateFixture();
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);
        fixture.Samples.Requests.Clear();

        var stop = await fixture.Connector.ExecuteAsync(
            StartTask("op-fifo-stop", "DEV-CNC-01", "stop"),
            CancellationToken.None);
        var start = await fixture.Connector.ExecuteAsync(
            StartTask("op-fifo-start", "DEV-CNC-01", "start"),
            CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.True(stop.Succeeded);
        Assert.True(start.Succeeded);
        var transitions = fixture.Samples.Requests
            .Where(request => request.DeviceAssetId == "DEV-CNC-01"
                && request.DeviceState is not null)
            .ToArray();
        Assert.Equal(["stopped", "running"], transitions.Select(request => request.DeviceState));
        Assert.Equal(2, transitions.Select(request => request.SourceSequence).Distinct().Count());
    }

    [Fact]
    public async Task Full_state_transition_queue_rejects_without_applying_or_acknowledging_the_command()
    {
        var fixture = CreateFixture(maxPendingStateTransitionsPerDevice: 2);
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);
        fixture.Samples.Requests.Clear();

        var stop = await fixture.Connector.ExecuteAsync(
            StartTask("op-capacity-stop", "DEV-CNC-01", "stop"),
            CancellationToken.None);
        var start = await fixture.Connector.ExecuteAsync(
            StartTask("op-capacity-start", "DEV-CNC-01", "start"),
            CancellationToken.None);
        var rejected = await fixture.Connector.ExecuteAsync(
            StartTask("op-capacity-rejected-stop", "DEV-CNC-01", "stop"),
            CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.True(stop.Succeeded);
        Assert.True(start.Succeeded);
        Assert.False(rejected.Succeeded);
        Assert.Equal("BadResourceUnavailable", rejected.Output["deviceReceiptCode"]);
        Assert.Equal(
            ["stopped", "running"],
            fixture.Samples.Requests
                .Where(request => request.DeviceAssetId == "DEV-CNC-01"
                    && request.DeviceState is not null)
                .Select(request => request.DeviceState));
    }

    [Fact]
    public async Task Retrying_initial_observation_does_not_consume_transition_queue_capacity()
    {
        var fixture = CreateFixture(
            maxPendingStateTransitionsPerDevice: 1,
            failDeliveries: true);
        await fixture.Connector.RunCollectionCycleAsync(CancellationToken.None);

        var stop = await fixture.Connector.ExecuteAsync(
            StartTask("op-capacity-after-initial-stop", "DEV-CNC-01", "stop"),
            CancellationToken.None);
        var rejectedStart = await fixture.Connector.ExecuteAsync(
            StartTask("op-capacity-after-initial-start", "DEV-CNC-01", "start"),
            CancellationToken.None);

        Assert.True(stop.Succeeded);
        Assert.False(rejectedStart.Succeeded);
        Assert.Equal(
            "BadResourceUnavailable",
            rejectedStart.Output["deviceReceiptCode"]);
    }

    [Theory]
    [MemberData(nameof(TerminalFailures))]
    public async Task Terminal_validation_paths_return_auditable_device_receipts(
        string instanceKey,
        IReadOnlyDictionary<string, string> parameters,
        string expectedReceiptCode)
    {
        var fixture = CreateFixture();
        var task = CreateTask($"op-{expectedReceiptCode}", instanceKey, parameters);

        var result = await fixture.Connector.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Retryable);
        Assert.Equal("validation", result.FailureCategory);
        AssertReceiptContext(
            result,
            task,
            instanceKey,
            instanceKey == "CONN-MODBUS-01" ? "modbus" : "opcua",
            parameters["commandType"],
            expectedReceiptCode);
    }

    [Fact]
    public async Task Receipt_cache_is_bounded_and_evicts_oldest_task_deterministically()
    {
        var fixture = CreateFixture(receiptCapacity: 2);

        await fixture.Connector.ExecuteAsync(StartTask("op-1", "DEV-CNC-01", "stop"), CancellationToken.None);
        await fixture.Connector.ExecuteAsync(StartTask("op-2", "DEV-CNC-02", "stop"), CancellationToken.None);
        await fixture.Connector.ExecuteAsync(StartTask("op-3", "DEV-CNC-03", "stop"), CancellationToken.None);

        Assert.Equal(2, fixture.Connector.CommandReceiptCount);
        Assert.Equal(["op-2", "op-3"], fixture.Connector.CachedOperationTaskIds);
    }

    [Fact]
    public void Full_route_scope_is_required_before_the_simulator_claims_a_control()
    {
        var fixture = CreateFixture();
        var valid = StartTask("op-route-1", "DEV-CNC-01", "stop");

        Assert.True(fixture.Connector.CanExecute(valid));
        Assert.False(fixture.Connector.CanExecute(valid with { OrganizationId = "other-org" }));
        Assert.False(fixture.Connector.CanExecute(valid with { EnvironmentId = "other-env" }));
        Assert.False(fixture.Connector.CanExecute(valid with { ConnectorHostId = "other-host" }));
        Assert.False(fixture.Connector.CanExecute(valid with { InstanceKey = "CONN-UNKNOWN-01" }));
    }

    public static TheoryData<string, IReadOnlyDictionary<string, string>, string> TerminalFailures => new()
    {
        {
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "write-tag",
                ["deviceAssetId"] = "DEV-UNKNOWN-01",
                ["tagKey"] = "spindle-speed",
                ["value"] = "2500"
            },
            "BadNotFound"
        },
        {
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "write-tag",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["tagKey"] = "unknown",
                ["value"] = "2500"
            },
            "BadNotFound"
        },
        {
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "write-tag",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["tagKey"] = "vibration",
                ["value"] = "3.0"
            },
            "BadNotSupported"
        },
        {
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "write-tag",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["tagKey"] = "spindle-speed",
                ["value"] = "9000"
            },
            "BadOutOfRange"
        },
        {
            "CONN-MODBUS-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "calibrate",
                ["deviceAssetId"] = "DEV-CTG-01"
            },
            "BadNotSupported"
        },
    };

    private static void AssertReceiptContext(
        ConnectorOperationExecution result,
        OperationTaskDispatchItem task,
        string connectorId,
        string protocol,
        string commandType,
        string receiptCode)
    {
        Assert.Equal(connectorId, result.Output["connectorId"]);
        Assert.Equal(protocol, result.Output["protocol"]);
        Assert.Equal(commandType, result.Output["commandType"]);
        Assert.Equal(task.OperationTaskId, result.Output["operationTaskId"]);
        Assert.Equal(task.CorrelationId, result.Output["correlationId"]);
        Assert.Equal(receiptCode, result.Output["deviceReceiptCode"]);
        Assert.False(string.IsNullOrWhiteSpace(result.Output["deviceReceiptMessage"]));
    }

    private static CommandFixture CreateFixture(
        int? receiptCapacity = null,
        DateTimeOffset? timestamp = null,
        int? maxPendingStateTransitionsPerDevice = null,
        bool failDeliveries = false)
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(
            (timestamp ?? DateTimeOffset.Parse("2026-07-26T00:00:00Z"))
            - clock.GetUtcNow());
        var options = SimulatedTestConfiguration.Bind(mutate: values =>
        {
            if (receiptCapacity.HasValue)
            {
                values["Simulated:CommandReceiptCacheCapacity"] =
                    receiptCapacity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (maxPendingStateTransitionsPerDevice.HasValue)
            {
                values["Simulated:MaxPendingStateTransitionsPerDevice"] =
                    maxPendingStateTransitionsPerDevice.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
            }
        });
        var samples = new RecordingSamplesClient(failDeliveries);
        var connector = new SimulatedConnector(
            options,
            new ConnectorHostRuntimeContext(
                "1.0",
                "1.0",
                "org-001",
                "env-dev",
                "connector-host-001",
                clock.GetUtcNow()),
            samples,
            clock);
        return new CommandFixture(connector, samples, clock);
    }

    private static OperationTaskDispatchItem StartTask(
        string operationTaskId,
        string deviceAssetId,
        string value) =>
        CreateTask(
            operationTaskId,
            "CONN-OPCUA-01",
            new Dictionary<string, string>
            {
                ["commandType"] = "start-stop",
                ["deviceAssetId"] = deviceAssetId,
                ["value"] = value
            });

    private static OperationTaskDispatchItem CreateTask(
        string operationTaskId,
        string instanceKey,
        IReadOnlyDictionary<string, string> parameters) =>
        new(
            operationTaskId,
            $"attempt-{operationTaskId}",
            "org-001",
            "env-dev",
            "connector-host-001",
            instanceKey,
            "device.control.command",
            $"corr-{operationTaskId}",
            parameters,
            $"lease-{operationTaskId}",
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T00:05:00Z"),
            1,
            300,
            3);

    private sealed record CommandFixture(
        SimulatedConnector Connector,
        RecordingSamplesClient Samples,
        ControllableTimeProvider Clock);

    private sealed class RecordingSamplesClient : IIndustrialTelemetrySamplesClient
    {
        private readonly bool _failDeliveries;

        public RecordingSamplesClient(bool failDeliveries)
        {
            _failDeliveries = failDeliveries;
        }

        public List<RecordIndustrialTelemetrySampleRequest> Requests { get; } = [];

        public Task RecordSampleAsync(
            RecordIndustrialTelemetrySampleRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (_failDeliveries)
            {
                throw new HttpRequestException("Simulated delivery failure.");
            }

            return Task.CompletedTask;
        }
    }
}
