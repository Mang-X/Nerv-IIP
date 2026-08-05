using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Host;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

/// <summary>
/// Every await here is bounded and every test carries the collection's <c>Timeout</c>: a collector
/// that never completes must fail with a reported condition, not park the test host (MAN-799).
/// </summary>
[Collection(HostTimeoutCollection.Name)]
public sealed class IndustrialTelemetryCollectorRunnerTests
{
    [Fact(Timeout = HostTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Run_collectors_continues_after_one_collector_fails()
    {
        var failing = new FailingCollector();
        var healthy = new RecordingCollector();
        var runner = new IndustrialTelemetryCollectorRunner(NullLogger<IndustrialTelemetryCollectorRunner>.Instance);

        await BoundedObservation.ObserveAsync(
            runner.RunCollectionCycleAsync([failing, healthy], CancellationToken.None),
            "the collection cycle to complete after one collector threw",
            () => $"failingAttempts={failing.Attempts}, healthyAttempts={healthy.Attempts}");

        Assert.Equal(1, failing.Attempts);
        Assert.Equal(1, healthy.Attempts);
    }

    [Fact(Timeout = HostTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task One_slow_collector_does_not_block_another_collector()
    {
        var slow = new BlockingCollector();
        var healthy = new RecordingCollector();
        var runner = new IndustrialTelemetryCollectorRunner(NullLogger<IndustrialTelemetryCollectorRunner>.Instance);

        var run = runner.RunCollectionCycleAsync([slow, healthy], CancellationToken.None);
        try
        {
            await BoundedObservation.ObserveAsync(
                slow.Started.Task,
                "the slow collector's cycle start",
                () => $"slowStarted={slow.Started.Task.IsCompleted}, healthyAttempts={healthy.Attempts}");

            Assert.Equal(1, healthy.Attempts);
        }
        finally
        {
            slow.Release();
        }

        await BoundedObservation.ObserveAsync(
            run,
            "the collection cycle to complete after the slow collector was released",
            () => $"slowReleased=true, healthyAttempts={healthy.Attempts}");
    }

    private sealed class FailingCollector : IIndustrialTelemetryCollectionConnector
    {
        public int Attempts { get; private set; }

        public Task RunCollectionCycleAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("simulated collector failure");
        }
    }

    private sealed class RecordingCollector : IIndustrialTelemetryCollectionConnector
    {
        public int Attempts { get; private set; }

        public Task RunCollectionCycleAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingCollector : IIndustrialTelemetryCollectionConnector
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunCollectionCycleAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }
}
