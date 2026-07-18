using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Host;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

public sealed class IndustrialTelemetryCollectorRunnerTests
{
    [Fact]
    public async Task Run_collectors_continues_after_one_collector_fails()
    {
        var failing = new FailingCollector();
        var healthy = new RecordingCollector();
        var runner = new IndustrialTelemetryCollectorRunner(NullLogger<IndustrialTelemetryCollectorRunner>.Instance);

        await runner.RunCollectionCycleAsync([failing, healthy], CancellationToken.None);

        Assert.Equal(1, failing.Attempts);
        Assert.Equal(1, healthy.Attempts);
    }

    [Fact]
    public async Task One_slow_collector_does_not_block_another_collector()
    {
        var slow = new BlockingCollector();
        var healthy = new RecordingCollector();
        var runner = new IndustrialTelemetryCollectorRunner(NullLogger<IndustrialTelemetryCollectorRunner>.Instance);

        var run = runner.RunCollectionCycleAsync([slow, healthy], CancellationToken.None);
        await slow.Started.Task;

        Assert.Equal(1, healthy.Attempts);

        slow.Release();
        await run;
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
