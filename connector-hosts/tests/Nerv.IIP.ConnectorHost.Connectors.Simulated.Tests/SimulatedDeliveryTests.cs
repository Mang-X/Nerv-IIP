using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.TestUtilities;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated.Tests;

public sealed class SimulatedDeliveryTests
{
    [Fact]
    public async Task Discovery_exposes_three_isolated_targets_alive_health_and_replace_style_manifests()
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(DateTimeOffset.Parse("2026-07-26T00:00:00Z") - clock.GetUtcNow());
        var reports = new RecordingReportSignal();
        var manifests = new RecordingManifestSignal();
        var connector = CreateConnector(
            SimulatedTestConfiguration.Bind(),
            new RecordingSamplesClient(clock),
            clock,
            reports,
            manifests);

        var initial = await connector.DiscoverAsync(CancellationToken.None);

        Assert.Equal(
            ["CONN-OPCUA-01", "CONN-MQTT-01", "CONN-MODBUS-01"],
            initial.Select(target => target.InstanceKey));
        Assert.All(initial, target => Assert.Equal("alive", target.CollectionHealth!.Connection!.Status));
        Assert.Equal(3, initial.Select(target => target.CollectionHealth!.CounterEpoch).Distinct().Count());
        Assert.Equal([44, 28, 24], initial.Select(target => target.TagManifest!.Entries.Count));
        Assert.All(initial, target => Assert.All(target.TagManifest!.Entries, entry => Assert.Equal("pending", entry.ActivationStatus)));

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        var collected = await connector.DiscoverAsync(CancellationToken.None);

        Assert.Equal([44L, 28L, 24L], collected.Select(target => target.CollectionHealth!.ReceivedCount));
        Assert.Equal([0L, 0L, 0L], collected.Select(target => target.CollectionHealth!.DroppedCount));
        Assert.Equal([0L, 0L, 0L], collected.Select(target => target.CollectionHealth!.ErrorCount));
        Assert.All(collected, target => Assert.Equal(clock.GetUtcNow(), target.CollectionHealth!.LastSampleAtUtc));
        Assert.All(collected, target => Assert.Equal(target.InstanceKey, target.TagManifest!.CollectionConnectorId));
        Assert.All(collected, target => Assert.All(target.TagManifest!.Entries, entry => Assert.Equal("active", entry.ActivationStatus)));
        Assert.Equal(
            ["CONN-MODBUS-01", "CONN-MQTT-01", "CONN-OPCUA-01"],
            reports.ConnectorIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CONN-MODBUS-01", "CONN-MQTT-01", "CONN-OPCUA-01"],
            manifests.ConnectorIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Failed_sample_retries_at_controlled_exponential_due_times_with_identical_payload()
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(DateTimeOffset.Parse("2026-07-26T00:00:00Z") - clock.GetUtcNow());
        const string sourceSequence = "simulated:CONN-OPCUA-01:DEV-CNC-01:vibration:0";
        var samples = new RecordingSamplesClient(clock, sourceSequence, failures: 2);
        var connector = CreateConnector(SimulatedTestConfiguration.Bind(), samples, clock);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(99));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(199));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var attempts = samples.Attempts
            .Where(attempt => HasSourcePrefix(
                attempt.Request.SourceSequence,
                sourceSequence))
            .ToArray();
        Assert.Equal(
            [
                DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-26T00:00:00.100Z"),
                DateTimeOffset.Parse("2026-07-26T00:00:00.300Z")
            ],
            attempts.Select(attempt => attempt.AtUtc));
        Assert.Equal(attempts[0].Request, attempts[1].Request);
        Assert.Equal(attempts[0].Request, attempts[2].Request);

        var opcUaHealth = (await connector.DiscoverAsync(CancellationToken.None))
            .Single(target => target.InstanceKey == "CONN-OPCUA-01")
            .CollectionHealth!;
        Assert.Equal(44, opcUaHealth.ReceivedCount);
        Assert.Equal(2, opcUaHealth.ErrorCount);
        Assert.Equal(0, opcUaHealth.DroppedCount);
        Assert.DoesNotContain(connector.PendingSampleCounts, pair => pair.Value != 0);
    }

    [Fact]
    public async Task Restart_within_the_same_cycle_replays_the_complete_request_payload()
    {
        var firstClock = At(DateTimeOffset.Parse("2026-07-26T00:00:00.100Z"));
        var secondClock = At(DateTimeOffset.Parse("2026-07-26T00:00:01.900Z"));
        var firstSamples = new RecordingSamplesClient(firstClock);
        var secondSamples = new RecordingSamplesClient(secondClock);
        var options = SimulatedTestConfiguration.Bind();

        await CreateConnector(options, firstSamples, firstClock)
            .RunCollectionCycleAsync(CancellationToken.None);
        await CreateConnector(options, secondSamples, secondClock)
            .RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(
            firstSamples.Attempts.Select(attempt => attempt.Request),
            secondSamples.Attempts.Select(attempt => attempt.Request));
    }

    [Fact]
    public async Task Non_caller_cancellation_retries_at_exact_due_times_and_drops_at_the_terminal_attempt()
    {
        var clock = At(DateTimeOffset.Parse("2026-07-26T00:00:00Z"));
        const string sourceSequence = "simulated:CONN-OPCUA-01:DEV-CNC-01:vibration:0";
        var samples = new RecordingSamplesClient(
            clock,
            sourceSequence,
            failures: 3,
            throwNonCallerCancellation: true);
        var connector = CreateConnector(SimulatedTestConfiguration.Bind(), samples, clock);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(99));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(199));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(
            [
                DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-26T00:00:00.100Z"),
                DateTimeOffset.Parse("2026-07-26T00:00:00.300Z")
            ],
            samples.Attempts
                .Where(attempt => HasSourcePrefix(
                    attempt.Request.SourceSequence,
                    sourceSequence))
                .Select(attempt => attempt.AtUtc));
        var health = (await connector.DiscoverAsync(CancellationToken.None))
            .Single(target => target.InstanceKey == "CONN-OPCUA-01")
            .CollectionHealth!;
        Assert.Equal(3, health.ErrorCount);
        Assert.Equal(1, health.DroppedCount);
        Assert.Equal(0, connector.PendingSampleCounts["CONN-OPCUA-01"]);
    }

    [Fact]
    public async Task State_transition_stays_on_the_retried_request_until_success_then_is_not_repeated()
    {
        var clock = At(DateTimeOffset.Parse("2026-07-26T00:00:00Z"));
        const string stateSourceSequence =
            "simulated:CONN-OPCUA-01:DEV-CNC-01:spindle-speed:1";
        var samples = new RecordingSamplesClient(
            clock,
            stateSourceSequence,
            failures: 1);
        var connector = CreateConnector(SimulatedTestConfiguration.Bind(), samples, clock);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        await connector.ExecuteAsync(
            CreateStartStopTask("op-retried-state-transition", "stop"),
            CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(2000));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(1900));
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var stateAttempts = samples.Attempts
            .Where(attempt => HasSourcePrefix(
                attempt.Request.SourceSequence,
                stateSourceSequence))
            .ToArray();
        Assert.Equal(2, stateAttempts.Length);
        Assert.All(stateAttempts, attempt =>
        {
            Assert.Equal("stopped", attempt.Request.DeviceState);
            Assert.Equal(
                DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
                attempt.Request.StateOccurredAtUtc);
        });
        Assert.DoesNotContain(
            samples.Attempts,
            attempt => attempt.Request.DeviceAssetId == "DEV-CNC-01"
                && HasSourcePrefix(
                    attempt.Request.SourceSequence,
                    "simulated:CONN-OPCUA-01:DEV-CNC-01:spindle-speed:2")
                && attempt.Request.DeviceState is not null);
    }

    [Fact]
    public async Task Pending_capacity_evicts_oldest_requests_and_accounts_drops_per_connector()
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(DateTimeOffset.Parse("2026-07-26T00:00:00Z") - clock.GetUtcNow());
        var options = SimulatedTestConfiguration.Bind(mutate: values =>
            values["Simulated:MaxPendingSamples"] = "2");
        var connector = CreateConnector(
            options,
            new RecordingSamplesClient(clock, failEveryAttempt: true),
            clock);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["CONN-OPCUA-01"] = 2,
                ["CONN-MQTT-01"] = 2,
                ["CONN-MODBUS-01"] = 2
            },
            connector.PendingSampleCounts);
        var health = (await connector.DiscoverAsync(CancellationToken.None))
            .ToDictionary(target => target.InstanceKey, target => target.CollectionHealth!, StringComparer.Ordinal);
        Assert.Equal(42, health["CONN-OPCUA-01"].DroppedCount);
        Assert.Equal(26, health["CONN-MQTT-01"].DroppedCount);
        Assert.Equal(22, health["CONN-MODBUS-01"].DroppedCount);
        Assert.Equal(2, health["CONN-OPCUA-01"].ErrorCount);
        Assert.Equal(2, health["CONN-MQTT-01"].ErrorCount);
        Assert.Equal(2, health["CONN-MODBUS-01"].ErrorCount);
    }

    [Fact]
    public async Task Cancellation_before_retry_due_stops_immediately_without_follow_up_attempt()
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(DateTimeOffset.Parse("2026-07-26T00:00:00Z") - clock.GetUtcNow());
        const string sourceSequence = "simulated:CONN-OPCUA-01:DEV-CNC-01:vibration:0";
        var samples = new RecordingSamplesClient(clock, sourceSequence, failures: 1);
        var connector = CreateConnector(SimulatedTestConfiguration.Bind(), samples, clock);
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.RunCollectionCycleAsync(cancellation.Token));

        Assert.Single(
            samples.Attempts,
            attempt => HasSourcePrefix(
                attempt.Request.SourceSequence,
                sourceSequence));
    }

    private static SimulatedConnector CreateConnector(
        SimulatedConnectorOptions options,
        IIndustrialTelemetrySamplesClient samplesClient,
        TimeProvider timeProvider,
        IConnectorReportSignal? reportSignal = null,
        IConnectorManifestSignal? manifestSignal = null) =>
        new(
            options,
            new ConnectorHostRuntimeContext(
                "1.0",
                "1.0",
                "org-001",
                "env-dev",
                "connector-host-001",
                timeProvider.GetUtcNow()),
            samplesClient,
            timeProvider,
            reportSignal,
            manifestSignal);

    private static ControllableTimeProvider At(DateTimeOffset timestamp)
    {
        var clock = new ControllableTimeProvider();
        clock.Advance(timestamp - clock.GetUtcNow());
        return clock;
    }

    private static OperationTaskDispatchItem CreateStartStopTask(
        string operationTaskId,
        string value) =>
        new(
            operationTaskId,
            $"attempt-{operationTaskId}",
            "org-001",
            "env-dev",
            "connector-host-001",
            "CONN-OPCUA-01",
            "device.control.command",
            $"correlation-{operationTaskId}",
            new Dictionary<string, string>
            {
                ["commandType"] = "start-stop",
                ["deviceAssetId"] = "DEV-CNC-01",
                ["value"] = value
            },
            $"lease-{operationTaskId}",
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T00:05:00Z"),
            1,
            300,
            3);

    private static bool HasSourcePrefix(
        string sourceSequence,
        string cycleSourceSequence) =>
        sourceSequence.StartsWith(
            $"{cycleSourceSequence}:",
            StringComparison.Ordinal);

    private sealed class RecordingSamplesClient : IIndustrialTelemetrySamplesClient
    {
        private readonly TimeProvider _timeProvider;
        private readonly string? _failingSourceSequence;
        private readonly bool _failEveryAttempt;
        private readonly bool _throwNonCallerCancellation;
        private int _remainingFailures;

        public RecordingSamplesClient(
            TimeProvider timeProvider,
            string? failingSourceSequence = null,
            int failures = 0,
            bool failEveryAttempt = false,
            bool throwNonCallerCancellation = false)
        {
            _timeProvider = timeProvider;
            _failingSourceSequence = failingSourceSequence;
            _remainingFailures = failures;
            _failEveryAttempt = failEveryAttempt;
            _throwNonCallerCancellation = throwNonCallerCancellation;
        }

        public List<(DateTimeOffset AtUtc, RecordIndustrialTelemetrySampleRequest Request)> Attempts { get; } = [];

        public Task RecordSampleAsync(
            RecordIndustrialTelemetrySampleRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add((_timeProvider.GetUtcNow(), request));
            if (_failEveryAttempt
                || _failingSourceSequence is not null
                && HasSourcePrefix(request.SourceSequence, _failingSourceSequence)
                && _remainingFailures-- > 0)
            {
                if (_throwNonCallerCancellation)
                {
                    throw new OperationCanceledException("Simulated downstream timeout.");
                }

                throw new HttpRequestException("IndustrialTelemetry unavailable.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReportSignal : IConnectorReportSignal
    {
        public List<string> ConnectorIds { get; } = [];
        public void Signal(string connectorId) => ConnectorIds.Add(connectorId);
        public Task<string?> WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingManifestSignal : IConnectorManifestSignal
    {
        public List<string> ConnectorIds { get; } = [];
        public void Signal(string connectorId) => ConnectorIds.Add(connectorId);
        public Task<ConnectorManifestSignalEvent?> WaitAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
