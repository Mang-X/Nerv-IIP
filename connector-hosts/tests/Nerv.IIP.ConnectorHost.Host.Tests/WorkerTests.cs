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

[Collection(HostTimeoutCollection.Name)]
public sealed class WorkerTests
{
    private static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The manifest retry ladder the Worker's <c>ConnectorManifestReportingLoop</c> produces after
    /// consecutive upload failures, in seconds. Every count the retry test needs is derived from
    /// this array so nothing has to be kept in sync by hand.
    /// </summary>
    private static readonly int[] ManifestRetryDelaySeconds = [1, 2, 4, 8, 16, 30, 30];

    private const int TestTimeoutMilliseconds = HostTimeoutCollection.TestTimeoutMilliseconds;

    /// <summary>
    /// Every await in this class goes through the shared bounded observation helper, so a lost
    /// fake-clock tick surfaces as a reported failure instead of parking the test — and therefore
    /// the whole test host — forever.
    /// </summary>
    private static Task ObserveAsync(
        Task observation,
        string condition,
        Func<string> lastObservation,
        TimeSpan? budget = null) =>
        BoundedObservation.ObserveAsync(observation, condition, lastObservation, budget);

    [Fact(Timeout = TestTimeoutMilliseconds)]
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
        try
        {
            await ObserveAsync(
                Task.WhenAll(collection.Started.Task, protocol.FirstCycle.Task, ops.Polled.Task),
                "first collection cycle start, first reporting cycle and first ops poll",
                () => $"collectionCalls={collection.Calls}, reportingCycles={protocol.ReportingCycles}, "
                    + $"opsCalls={ops.Calls}");
            await ObserveAsync(
                clock.WaitForTimerEverCreatedAsync(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4)),
                "connection-monitor periodic timer registration (due=4s, period=4s)",
                () => $"monitorCalls={monitor.Calls}, now={clock.GetUtcNow():O}");
            Assert.Equal(0, monitor.Calls);

            var tracker = new ConnectorConnectionStateTracker("connector-a", clock, signal.Signal);
            tracker.MarkLost("transport", "socket-closed");
            await ObserveAsync(
                protocol.SecondCycle.Task,
                "second reporting cycle after the signalled connection loss",
                () => $"reportingCycles={protocol.ReportingCycles}, now={clock.GetUtcNow():O}");
            Assert.Equal(DateTimeOffset.Parse("2026-07-17T00:00:00Z"), clock.GetUtcNow());

            clock.Advance(TimeSpan.FromSeconds(4));
            await ObserveAsync(
                monitor.Checked.Task,
                "first connection check",
                () => $"monitorCalls={monitor.Calls}, now={clock.GetUtcNow():O}");
            await ObserveAsync(
                protocol.ThirdCycle.Task,
                "third reporting cycle",
                () => $"reportingCycles={protocol.ReportingCycles}, now={clock.GetUtcNow():O}");

            Assert.True(monitor.Calls >= 1);
            Assert.True(protocol.ReportingCycles >= 3);
            Assert.True(ops.Calls >= 1);
            Assert.False(collection.Completed);

            collection.Release();
            await ObserveAsync(
                collection.Finished.Task,
                "released collection cycle completion",
                () => $"collectionCalls={collection.Calls}, completed={collection.Completed}");
        }
        finally
        {
            collection.Release();
            await worker.StopAsync(CancellationToken.None).WaitAsync(StopBudget);
        }
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
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
            await ObserveAsync(
                Task.WhenAll(protocol.FirstCycle.Task, ops.Polled.Task),
                "first reporting cycle and first ops poll",
                () => $"reportingCycles={protocol.ReportingCycles}, opsCalls={ops.Calls}");

            // The connection-monitor loop is started while `Task.WhenAll` enumerates the worker's
            // loop list, which happens *after* the eagerly evaluated reporting/ops loops have
            // already released `FirstCycle`/`Polled`. Advancing the clock before the periodic
            // timer is registered silently drops the 4s tick — the timer is then created at the
            // already-advanced "now" and nothing ever fires it again. Wait for the registration.
            await ObserveAsync(
                clock.WaitForTimerEverCreatedAsync(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4)),
                "connection-monitor periodic timer registration (due=4s, period=4s)",
                () => $"monitorCalls={monitor.Calls}, now={clock.GetUtcNow():O}");

            clock.Advance(TimeSpan.FromSeconds(4));
            await ObserveAsync(
                monitor.FirstCheckStarted.Task,
                "first connection check start",
                () => $"monitorCalls={monitor.Calls}, now={clock.GetUtcNow():O}");
            Assert.Equal(1, monitor.Calls);

            monitor.CompleteFirstCheck();
            await ObserveAsync(
                monitor.SecondCheckStarted.Task,
                "second connection check start",
                () => $"monitorCalls={monitor.Calls}, startedAt={string.Join(",", monitor.StartedAtUtc)}");

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
            await worker.StopAsync(CancellationToken.None).WaitAsync(StopBudget);
        }
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
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
            await ObserveAsync(
                Task.WhenAll(protocol.FirstCycle.Task, manifestClient.Started.Task),
                "first reporting cycle and first manifest upload start",
                () => $"reportingCycles={protocol.ReportingCycles}, manifestCompleted={manifestClient.Completed}");
            signal.Signal("connector-a");

            await ObserveAsync(
                protocol.SecondCycle.Task,
                "second reporting cycle while the manifest upload is still blocked",
                () => $"reportingCycles={protocol.ReportingCycles}, manifestCompleted={manifestClient.Completed}");
            Assert.False(manifestClient.Completed);
        }
        finally
        {
            manifestClient.Release();
            await worker.StopAsync(CancellationToken.None).WaitAsync(StopBudget);
        }
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task Manifest_retry_loop_uses_exact_exponential_due_times_instead_of_heartbeat_quantization()
    {
        var clock = new ControllableTimeProvider();

        // One attempt before the ladder plus one per rung: attempt 0 fails, then each of the
        // `ManifestRetryDelaySeconds` waits is followed by exactly one more attempt.
        var expectedAttempts = ManifestRetryDelaySeconds.Length + 1;
        var manifestClient = new TimedFailingManifestClient(clock, expectedAttempts);

        // The retry wait is armed *after* the failing attempt completes. Advancing the fake clock
        // before that arming would create the retry timer at the already-advanced "now" — the same
        // lost-tick race that hung this assembly in MAN-799. `Task.Delay(..., timeProvider, ...)`
        // is created synchronously inside the signal's `WaitAsync`, before its first await, so the
        // wrapper observing that call is an exact barrier for "the retry timer now exists".
        // One armed wait per attempt, so the observation array is the same size as the attempts.
        var manifestSignal = new RecordingManifestSignal(
            new ConnectorManifestSignal(),
            expectedWaits: expectedAttempts);
        var worker = CreateWorker(
            clock,
            new ConnectorReportSignal(),
            new RecordingProtocolClient(),
            new RecordingOpsClient(),
            [],
            [],
            manifestClient,
            manifestSignal);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await ObserveAsync(
                manifestClient.Attempt(0),
                "first manifest upload attempt",
                () => $"attempts={manifestClient.AttemptTimesUtc.Count}, now={clock.GetUtcNow():O}");
            await ObserveAsync(
                manifestSignal.WaitArmed(0),
                "first retry wait armed (retry timer registered on the fake clock)",
                () => $"waitsArmed={manifestSignal.ArmedWaits}, attempts={manifestClient.AttemptTimesUtc.Count}");

            for (var index = 0; index < ManifestRetryDelaySeconds.Length; index++)
            {
                clock.Advance(TimeSpan.FromSeconds(ManifestRetryDelaySeconds[index]) - TimeSpan.FromTicks(1));
                Assert.False(manifestClient.Attempt(index + 1).IsCompleted);
                clock.Advance(TimeSpan.FromTicks(1));
                await ObserveAsync(
                    manifestClient.Attempt(index + 1),
                    $"manifest upload attempt {index + 1}",
                    () => $"attempts={manifestClient.AttemptTimesUtc.Count}, now={clock.GetUtcNow():O}");
                await ObserveAsync(
                    manifestSignal.WaitArmed(index + 1),
                    $"retry wait {index + 1} armed (next retry timer registered on the fake clock)",
                    () => $"waitsArmed={manifestSignal.ArmedWaits}, attempts={manifestClient.AttemptTimesUtc.Count}, "
                        + $"now={clock.GetUtcNow():O}");
            }

            // Independently written out rather than re-derived from ManifestRetryDelaySeconds: the
            // running sum of the ladder is exactly what is under test, so restating it as literals
            // keeps the assertion from agreeing with the loop that drove the clock.
            Assert.Equal(
                new[] { 0, 1, 3, 7, 15, 31, 61, 91 },
                manifestClient.AttemptTimesUtc
                    .Select(attempt => (int)(attempt - DateTimeOffset.Parse("2026-07-17T00:00:00Z")).TotalSeconds)
                    .ToArray());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(StopBudget);
        }
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
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
            await ObserveAsync(
                manifestClient.Attempt(0),
                "first manifest upload attempt",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}");

            manifestSignal.Signal("connector-a");
            await ObserveAsync(
                connector.Discovery(1),
                "second connector discovery after the activation signal",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}");
            Assert.Single(manifestClient.Requests);

            ((IConnectorManifestRebirthRequest)manifestSignal).RequestRebirth("connector-a");
            await ObserveAsync(
                manifestClient.Attempt(1),
                "republished manifest upload after the explicit rebirth request",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}");

            Assert.Equal(manifestClient.Requests[0].ManifestRevision, manifestClient.Requests[1].ManifestRevision);
            Assert.Equal(
                manifestClient.Requests[0].ManifestObservedAtUtc.AddTicks(1),
                manifestClient.Requests[1].ManifestObservedAtUtc);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(StopBudget);
        }
    }

    [Fact(Timeout = TestTimeoutMilliseconds)]
    public async Task Bulk_activation_signals_for_one_connector_trigger_only_one_additional_manifest_scan()
    {
        var clock = new ControllableTimeProvider();
        // Only the first two waits are observed: #0 drains the collapsed activation signals, #1 is
        // the wait that must still be blocked once the single extra scan has happened.
        var manifestSignal = new RecordingManifestSignal(new ConnectorManifestSignal(), expectedWaits: 2);
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
            await ObserveAsync(
                manifestClient.Started.Task,
                "first manifest upload start",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}");
            for (var index = 0; index < 500; index++)
            {
                manifestSignal.Signal("connector-a");
            }

            manifestClient.Release();
            await ObserveAsync(
                manifestClient.Completed.Task,
                "released manifest upload completion",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}");
            await ObserveAsync(
                connector.Discovery(1),
                "second connector discovery collapsing the 500 activation signals",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}");
            await ObserveAsync(
                manifestSignal.WaitEntered(1),
                "second manifest signal wait entered",
                () => $"requests={manifestClient.Requests.Count}, discoveries={connector.DiscoveryCount}, "
                    + $"enteredWaits={manifestSignal.EnteredWaits}, armedWaits={manifestSignal.ArmedWaits}");

            Assert.False(manifestSignal.WaitTask(1).IsCompleted);
            Assert.Equal(2, connector.DiscoveryCount);
            Assert.Single(manifestClient.Requests);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(StopBudget);
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
        IConnectorManifestSignal? manifestSignal = null,
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

    /// <summary>
    /// The one recording decorator for <see cref="IConnectorManifestSignal"/>. It publishes two
    /// distinct observation streams per wait, because the two facts are not the same fact:
    ///
    /// <list type="bullet">
    /// <item><description><c>WaitEntered(i)</c> — the i-th <c>WaitAsync</c> call returned control,
    /// whether or not it blocked. <c>WaitTask(i)</c> exposes that call's underlying task.</description></item>
    /// <item><description><c>WaitArmed(i)</c> — the i-th call that actually registered a retry
    /// timer on the injected <see cref="TimeProvider"/>. This is the barrier a test must cross
    /// before advancing the fake clock.</description></item>
    /// </list>
    ///
    /// <para>The distinction matters: <c>ConnectorManifestSignal.WaitAsync</c> returns early from
    /// <c>TryTakePending</c> when a signal is already queued, and that path never reaches
    /// <c>Task.Delay</c> — no timer is created, so treating it as "armed" would let a test advance
    /// the clock before any timer exists, which is precisely the MAN-799 lost-tick race.</para>
    ///
    /// <para>Arming is detected from the returned task still being incomplete. The early-return
    /// path always yields an already-completed task, and the timed path creates its
    /// <c>Task.Delay</c> timer synchronously before the first await, so an incomplete task proves
    /// the timer exists. The converse is deliberately not assumed: a timed wait that also happened
    /// to complete synchronously (only reachable if a signal races in) is simply not reported as
    /// armed. That errs toward a bounded, reported observation timeout instead of toward the
    /// premature <c>Advance</c> this barrier exists to prevent.</para>
    /// </summary>
    private sealed class RecordingManifestSignal(IConnectorManifestSignal inner, int expectedWaits)
        : IConnectorManifestSignal
    {
        private readonly TaskCompletionSource[] _entered = CreateObservations(expectedWaits);
        private readonly TaskCompletionSource[] _armed = CreateObservations(expectedWaits);
        private readonly Task<ConnectorManifestSignalEvent?>?[] _waitTasks =
            new Task<ConnectorManifestSignalEvent?>?[expectedWaits];
        private int _enteredCount;
        private int _armedCount;

        public int EnteredWaits => Volatile.Read(ref _enteredCount);

        public int ArmedWaits => Volatile.Read(ref _armedCount);

        public Task WaitEntered(int index) => _entered[index].Task;

        public Task WaitArmed(int index) => _armed[index].Task;

        public Task<ConnectorManifestSignalEvent?> WaitTask(int index) =>
            Volatile.Read(ref _waitTasks[index])
            ?? throw new InvalidOperationException($"Manifest signal wait #{index} has not started.");

        public void Signal(string connectorId) => inner.Signal(connectorId);

        public async Task<ConnectorManifestSignalEvent?> WaitAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            var underlyingTask = inner.WaitAsync(timeout, timeProvider, cancellationToken);
            var armed = !underlyingTask.IsCompleted;

            var enteredIndex = Interlocked.Increment(ref _enteredCount) - 1;
            if (enteredIndex < _entered.Length)
            {
                // Published before the observation is raised, so an awaiter of `WaitEntered` can
                // always read the matching `WaitTask`.
                Volatile.Write(ref _waitTasks[enteredIndex], underlyingTask);
                _entered[enteredIndex].TrySetResult();
            }

            if (armed)
            {
                var armedIndex = Interlocked.Increment(ref _armedCount) - 1;
                if (armedIndex < _armed.Length)
                {
                    _armed[armedIndex].TrySetResult();
                }
            }

            return await underlyingTask;
        }

        private static TaskCompletionSource[] CreateObservations(int count) =>
            Enumerable.Range(0, count)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                .ToArray();
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
        public TaskCompletionSource SecondCheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunConnectionCheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            StartedAtUtc.Add(clock.GetUtcNow());
            if (Calls != 1)
            {
                // Publishing the observation lets the caller assert on a completed second check
                // instead of assuming the resumed loop already ran on its thread.
                SecondCheckStarted.TrySetResult();
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
            return Task.FromResult(new ApplicationRegistrationResult("registration-a", registration.InstanceKey, "token-a"));
        }

        public Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default)
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

            return Task.CompletedTask;
        }
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
