using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Host;
using Nerv.IIP.ConnectorHost.TestUtilities;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.ConnectorProtocol;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

[CollectionDefinition("Worker scheduling", DisableParallelization = true)]
public sealed class WorkerSchedulingCollection;

[Collection("Worker scheduling")]
public sealed class WorkerTests
{
    [Fact]
    public async Task Connection_monitor_reporting_and_ops_run_while_collection_is_blocked()
    {
        var clock = new ControllableTimeProvider();
        var signal = new ConnectorReportSignal();
        var collection = new BlockingCollector();
        var monitor = new RecordingConnectionMonitor();
        var protocol = new RecordingProtocolClient();
        var ops = new RecordingOpsClient();
        var worker = CreateWorker(clock, signal, protocol, ops, [collection], [monitor]);

        await worker.StartAsync(CancellationToken.None);
        await Task.WhenAll(collection.Started.Task, protocol.FirstCycle.Task, ops.Polled.Task);
        Assert.Equal(0, monitor.Calls);

        var tracker = new ConnectorConnectionStateTracker("connector-a", clock, signal.Signal);
        tracker.MarkLost("transport", "socket-closed");
        await protocol.SecondCycle.Task;
        Assert.Equal(DateTimeOffset.Parse("2026-07-17T00:00:00Z"), clock.GetUtcNow());

        clock.Advance(TimeSpan.FromSeconds(4));
        await Task.WhenAll(monitor.Checked.Task, protocol.ThirdCycle.Task);

        Assert.True(monitor.Calls >= 1);
        Assert.True(protocol.ReportingCycles >= 3);
        Assert.True(ops.Calls >= 1);
        Assert.False(collection.Completed);

        collection.Release();
        await collection.Finished.Task;
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Slow_connection_monitor_keeps_the_next_start_on_the_fixed_four_second_tick()
    {
        var clock = new ControllableTimeProvider();
        var monitor = new SlowConnectionMonitor(clock);
        var protocol = new RecordingProtocolClient();
        var ops = new RecordingOpsClient();
        var worker = CreateWorker(clock, new ConnectorReportSignal(), protocol, ops, [], [monitor]);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(protocol.FirstCycle.Task, ops.Polled.Task);

            clock.Advance(TimeSpan.FromSeconds(4));
            await monitor.FirstCheckStarted.Task;
            Assert.Equal(1, monitor.Calls);
            monitor.CompleteFirstCheck();

            Assert.Equal(2, monitor.Calls);
            Assert.Equal(
                [
                    DateTimeOffset.Parse("2026-07-17T00:00:04Z"),
                    DateTimeOffset.Parse("2026-07-17T00:00:08Z")
                ],
                monitor.StartedAtUtc);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Blocked_manifest_upload_does_not_delay_heartbeat_reporting()
    {
        var clock = new ControllableTimeProvider();
        var signal = new ConnectorReportSignal();
        var protocol = new RecordingProtocolClient();
        var manifestClient = new BlockingManifestClient();
        var worker = CreateWorker(clock, signal, protocol, new RecordingOpsClient(), [], [], manifestClient);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(protocol.FirstCycle.Task, manifestClient.Started.Task);
            signal.Signal("connector-a");

            await protocol.SecondCycle.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(manifestClient.Completed);
        }
        finally
        {
            manifestClient.Release();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Manifest_retry_loop_uses_exact_exponential_due_times_instead_of_heartbeat_quantization()
    {
        var clock = new ControllableTimeProvider();
        var manifestClient = new TimedFailingManifestClient(clock, expectedAttempts: 8);
        var worker = CreateWorker(
            clock,
            new ConnectorReportSignal(),
            new RecordingProtocolClient(),
            new RecordingOpsClient(),
            [],
            [],
            manifestClient);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await manifestClient.Attempt(0).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            var retryDelays = new[] { 1, 2, 4, 8, 16, 30, 30 };
            for (var index = 0; index < retryDelays.Length; index++)
            {
                clock.Advance(TimeSpan.FromSeconds(retryDelays[index]) - TimeSpan.FromTicks(1));
                Assert.False(manifestClient.Attempt(index + 1).IsCompleted);
                clock.Advance(TimeSpan.FromTicks(1));
                try
                {
                    await manifestClient.Attempt(index + 1).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException exception)
                {
                    throw new InvalidOperationException(
                        $"Attempt {index + 1} was not observed at {clock.GetUtcNow():O}; observed {manifestClient.AttemptTimesUtc.Count} attempts.",
                        exception);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            Assert.Equal(
                new[] { 0, 1, 3, 7, 15, 31, 61, 91 },
                manifestClient.AttemptTimesUtc
                    .Select(attempt => (int)(attempt - DateTimeOffset.Parse("2026-07-17T00:00:00Z")).TotalSeconds)
                    .ToArray());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Explicit_rebirth_request_republishes_matching_root_but_activation_signal_does_not()
    {
        var clock = new ControllableTimeProvider();
        var manifestSignal = new ConnectorManifestSignal();
        var connector = new ObservableStaticConnector();
        var manifestClient = new RecordingAcknowledgingManifestClient(expectedAttempts: 2);
        var worker = CreateWorker(
            clock,
            new ConnectorReportSignal(),
            new RecordingProtocolClient(),
            new RecordingOpsClient(),
            [],
            [],
            manifestClient,
            manifestSignal,
            connector);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await manifestClient.Attempt(0).WaitAsync(TimeSpan.FromSeconds(5));

            manifestSignal.Signal("connector-a");
            await connector.Discovery(1).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(manifestClient.Requests);

            ((IConnectorManifestRebirthRequest)manifestSignal).RequestRebirth("connector-a");
            await manifestClient.Attempt(1).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(manifestClient.Requests[0].ManifestRevision, manifestClient.Requests[1].ManifestRevision);
            Assert.Equal(
                manifestClient.Requests[0].ManifestObservedAtUtc.AddTicks(1),
                manifestClient.Requests[1].ManifestObservedAtUtc);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Bulk_activation_signals_for_one_connector_trigger_only_one_additional_manifest_scan()
    {
        var clock = new ControllableTimeProvider();
        var manifestSignal = new ConnectorManifestSignal();
        var connector = new ObservableStaticConnector();
        var manifestClient = new BlockingInitialAcknowledgementManifestClient();
        var worker = CreateWorker(
            clock,
            new ConnectorReportSignal(),
            new RecordingProtocolClient(),
            new RecordingOpsClient(),
            [],
            [],
            manifestClient,
            manifestSignal,
            connector);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await manifestClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < 500; index++)
            {
                manifestSignal.Signal("connector-a");
            }

            manifestClient.Release();
            await manifestClient.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await connector.Discovery(1).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            Assert.Equal(2, connector.DiscoveryCount);
            Assert.Single(manifestClient.Requests);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void Governed_worker_profile_rejects_invalid_values(ConnectorHostWorkerOptions options)
    {
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    public static TheoryData<ConnectorHostWorkerOptions> InvalidProfiles => new()
    {
        ValidOptions(heartbeatSeconds: 3),
        ValidOptions(connectionProbeSeconds: 0),
        ValidOptions(connectionProbeSeconds: 1),
        ValidOptions(connectionProbeSeconds: 3),
        ValidOptions(connectionProbeSeconds: 5),
        ValidOptions(collectionCycleSeconds: 0),
        ValidOptions(operationPollSeconds: 0),
        ValidOptions(connectionDetectionBudgetSeconds: 5),
        ValidOptions(backendDeadlineSeconds: 9)
    };

    private static Worker CreateWorker(
        TimeProvider timeProvider,
        IConnectorReportSignal signal,
        RecordingProtocolClient protocol,
        RecordingOpsClient ops,
        IReadOnlyList<IIndustrialTelemetryCollectionConnector> collectors,
        IReadOnlyList<IConnectorConnectionMonitor> monitors,
        IConnectorTagManifestClient? manifestClient = null,
        ConnectorManifestSignal? manifestSignal = null,
        IConnector? manifestConnector = null)
    {
        var connector = new StaticConnector();
        manifestConnector ??= new StaticConnector(includeManifest: manifestClient is not null);
        manifestClient ??= new NoOpManifestClient();
        var reporter = new ConnectorManifestReporter(manifestClient, ConnectorHostRuntimeContext.DefaultLocal, timeProvider);
        var reporting = new ConnectorReportingLoop([connector], protocol, ConnectorHostRuntimeContext.DefaultLocal);
        var manifestReporting = new ConnectorManifestReportingLoop([manifestConnector], reporter);
        var operations = new ConnectorOperationLoop([], ops, ConnectorHostRuntimeContext.DefaultLocal);
        return new Worker(
            NullLogger<Worker>.Instance,
            ValidOptions(),
            timeProvider,
            reporting,
            manifestReporting,
            operations,
            new IndustrialTelemetryCollectorRunner(NullLogger<IndustrialTelemetryCollectorRunner>.Instance),
            collectors,
            monitors,
            signal,
            manifestSignal ?? new ConnectorManifestSignal());
    }

    private static ConnectorHostWorkerOptions ValidOptions(
        int heartbeatSeconds = 2,
        int connectionProbeSeconds = 4,
        int collectionCycleSeconds = 30,
        int operationPollSeconds = 30,
        int connectionDetectionBudgetSeconds = 4,
        int backendDeadlineSeconds = 8) => new()
        {
            HeartbeatSeconds = heartbeatSeconds,
            ConnectionProbeSeconds = connectionProbeSeconds,
            CollectionCycleSeconds = collectionCycleSeconds,
            OperationPollSeconds = operationPollSeconds,
            ConnectionDetectionBudgetSeconds = connectionDetectionBudgetSeconds,
            BackendDeadlineSeconds = backendDeadlineSeconds
        };

    private sealed class StaticConnector(bool includeManifest = false) : IConnector
    {
        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ConnectorTarget> targets =
            [
                new(
                    "node-a", "Node A", "test", "collector", "Collector", "1.0", "connector-a", "Connector A", "running", "degraded", [], new Dictionary<string, string>(),
                    TagManifest: includeManifest
                        ? new ConnectorTagManifestSnapshot(
                            "connector-a",
                            "opcua",
                            [new ConnectorTagManifestEntrySnapshot("device-a", "temperature", true, "ns=2;s=T", "pending", DateTimeOffset.Parse("2026-07-17T00:00:00Z"))])
                        : null)
            ];
            return Task.FromResult(targets);
        }
    }

    private sealed class ObservableStaticConnector : IConnector
    {
        private readonly TaskCompletionSource[] _discoveries = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        private int _discoveryCount;

        public int DiscoveryCount => Volatile.Read(ref _discoveryCount);

        public Task Discovery(int index) => _discoveries[index].Task;

        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _discoveryCount) - 1;
            if (index < _discoveries.Length)
            {
                _discoveries[index].TrySetResult();
            }

            IReadOnlyList<ConnectorTarget> targets =
            [
                new(
                    "node-a", "Node A", "test", "collector", "Collector", "1.0", "connector-a", "Connector A", "running", "degraded", [], new Dictionary<string, string>(),
                    TagManifest: new ConnectorTagManifestSnapshot(
                        "connector-a",
                        "opcua",
                        [new ConnectorTagManifestEntrySnapshot("device-a", "temperature", true, "ns=2;s=T", "pending", DateTimeOffset.Parse("2026-07-17T00:00:00Z"))]))
            ];
            return Task.FromResult(targets);
        }
    }

    private sealed class BlockingCollector : IIndustrialTelemetryCollectionConnector
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public bool Completed { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunCollectionCycleAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            Completed = true;
            Finished.TrySetResult();
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingConnectionMonitor : IConnectorConnectionMonitor
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunConnectionCheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Checked.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class SlowConnectionMonitor(ControllableTimeProvider clock) : IConnectorConnectionMonitor
    {
        private readonly TaskCompletionSource _completeFirstCheck = new();

        public int Calls { get; private set; }
        public List<DateTimeOffset> StartedAtUtc { get; } = [];
        public TaskCompletionSource FirstCheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunConnectionCheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            StartedAtUtc.Add(clock.GetUtcNow());
            if (Calls != 1)
            {
                return;
            }

            clock.Advance(TimeSpan.FromSeconds(4));
            FirstCheckStarted.TrySetResult();
            await _completeFirstCheck.Task.WaitAsync(cancellationToken);
        }

        public void CompleteFirstCheck() => _completeFirstCheck.TrySetResult();
    }

    private sealed class RecordingProtocolClient : IConnectorProtocolClient
    {
        public int ReportingCycles { get; private set; }
        public TaskCompletionSource FirstCycle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCycle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThirdCycle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ApplicationRegistrationResult> SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default)
        {
            ReportingCycles++;
            if (ReportingCycles == 1)
            {
                FirstCycle.TrySetResult();
            }
            else if (ReportingCycles == 2)
            {
                SecondCycle.TrySetResult();
            }
            else if (ReportingCycles == 3)
            {
                ThirdCycle.TrySetResult();
            }

            return Task.FromResult(new ApplicationRegistrationResult("registration-a", registration.InstanceKey, "token-a"));
        }

        public Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingOpsClient : IOpsClient
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Polled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PendingOperationTasksResponse> ClaimOperationTasksAsync(ClaimOperationTasksRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Polled.TrySetResult();
            return Task.FromResult(new PendingOperationTasksResponse([]));
        }

        public Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default) =>
            ClaimOperationTasksAsync(new ClaimOperationTasksRequest(organizationId, environmentId, connectorHostId, take), cancellationToken);

        public Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> ApproveOperationTaskAsync(string operationTaskId, DecideOperationApprovalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> RejectOperationTaskAsync(string operationTaskId, DecideOperationApprovalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> AbandonOperationTaskLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> HeartbeatOperationTaskLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingManifestClient : IConnectorTagManifestClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed { get; private set; }

        public async Task<ConnectorTagManifestAcknowledgement> ReportAsync(
            ConnectorTagManifestReport report,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            Completed = true;
            return new ConnectorTagManifestAcknowledgement("accepted", report.ManifestRevision, report.ManifestObservedAtUtc);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class NoOpManifestClient : IConnectorTagManifestClient
    {
        public Task<ConnectorTagManifestAcknowledgement> ReportAsync(
            ConnectorTagManifestReport report,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ConnectorTagManifestAcknowledgement("accepted", report.ManifestRevision, report.ManifestObservedAtUtc));
    }

    private sealed class TimedFailingManifestClient(TimeProvider timeProvider, int expectedAttempts) : IConnectorTagManifestClient
    {
        private readonly TaskCompletionSource[] _attempts = Enumerable.Range(0, expectedAttempts)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();

        public List<DateTimeOffset> AttemptTimesUtc { get; } = [];

        public Task Attempt(int index) => _attempts[index].Task;

        public Task<ConnectorTagManifestAcknowledgement> ReportAsync(
            ConnectorTagManifestReport report,
            CancellationToken cancellationToken)
        {
            var index = AttemptTimesUtc.Count;
            AttemptTimesUtc.Add(timeProvider.GetUtcNow());
            if (index < _attempts.Length)
            {
                _attempts[index].TrySetResult();
            }

            return Task.FromException<ConnectorTagManifestAcknowledgement>(new HttpRequestException("unavailable"));
        }
    }

    private sealed class RecordingAcknowledgingManifestClient(int expectedAttempts) : IConnectorTagManifestClient
    {
        private readonly TaskCompletionSource[] _attempts = Enumerable.Range(0, expectedAttempts)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();

        public List<ConnectorTagManifestReport> Requests { get; } = [];

        public Task Attempt(int index) => _attempts[index].Task;

        public Task<ConnectorTagManifestAcknowledgement> ReportAsync(
            ConnectorTagManifestReport report,
            CancellationToken cancellationToken)
        {
            var index = Requests.Count;
            Requests.Add(report);
            if (index < _attempts.Length)
            {
                _attempts[index].TrySetResult();
            }

            return Task.FromResult(new ConnectorTagManifestAcknowledgement("accepted", report.ManifestRevision, report.ManifestObservedAtUtc));
        }
    }

    private sealed class BlockingInitialAcknowledgementManifestClient : IConnectorTagManifestClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ConnectorTagManifestReport> Requests { get; } = [];

        public async Task<ConnectorTagManifestAcknowledgement> ReportAsync(
            ConnectorTagManifestReport report,
            CancellationToken cancellationToken)
        {
            Requests.Add(report);
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            Completed.TrySetResult();
            return new ConnectorTagManifestAcknowledgement("accepted", report.ManifestRevision, report.ManifestObservedAtUtc);
        }

        public void Release() => _release.TrySetResult();
    }

}
