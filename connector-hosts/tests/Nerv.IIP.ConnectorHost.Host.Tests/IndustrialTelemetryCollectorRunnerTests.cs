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
}
