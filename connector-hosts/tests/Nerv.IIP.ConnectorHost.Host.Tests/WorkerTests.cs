using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Host;
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
        IReadOnlyList<IConnectorConnectionMonitor> monitors)
    {
        var reporting = new ConnectorReportingLoop([new StaticConnector()], protocol, ConnectorHostRuntimeContext.DefaultLocal);
        var operations = new ConnectorOperationLoop([], ops, ConnectorHostRuntimeContext.DefaultLocal);
        return new Worker(
            NullLogger<Worker>.Instance,
            ValidOptions(),
            timeProvider,
            reporting,
            operations,
            new IndustrialTelemetryCollectorRunner(NullLogger<IndustrialTelemetryCollectorRunner>.Instance),
            collectors,
            monitors,
            signal);
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

    private sealed class StaticConnector : IConnector
    {
        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ConnectorTarget> targets =
            [
                new("node-a", "Node A", "test", "collector", "Collector", "1.0", "connector-a", "Connector A", "running", "degraded", [], new Dictionary<string, string>())
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

    private sealed class ControllableTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ControllableTimer> _timers = [];
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-17T00:00:00Z");

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControllableTimer(this, callback, state, dueTime, period);
            lock (_gate)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            ControllableTimer[] due;
            lock (_gate)
            {
                _utcNow += amount;
                due = _timers.Where(timer => timer.IsDue(_utcNow)).ToArray();
            }

            foreach (var timer in due)
            {
                timer.Fire(GetUtcNow());
            }
        }

        private sealed class ControllableTimer(
            ControllableTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private DateTimeOffset? _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            private TimeSpan _period = period;
            private bool _disposed;

            public bool IsDue(DateTimeOffset utcNow) => !_disposed && _dueAtUtc <= utcNow;

            public void Fire(DateTimeOffset utcNow)
            {
                if (_disposed || _dueAtUtc > utcNow)
                {
                    return;
                }

                _dueAtUtc = _period == Timeout.InfiniteTimeSpan ? null : utcNow + _period;
                callback(state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
                return true;
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
