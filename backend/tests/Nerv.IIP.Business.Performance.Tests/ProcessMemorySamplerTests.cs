using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class ProcessMemorySamplerTests
{
    [Fact]
    public void Start_rejects_non_positive_sampling_interval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessMemorySampler.Start(TimeSpan.Zero));
    }

    [Fact]
    public async Task StopAsync_supports_configurable_interval_and_concurrent_callers()
    {
        await using var sampler = ProcessMemorySampler.Start(TimeSpan.FromMilliseconds(10));

        // The loop runs on the real clock (it samples a real process), so the observable fact is the
        // sample itself, not an elapsed duration guessed from the configured interval.
        await TestTimeout.RunAsync(
            "process-memory sampler to take its first interval sample",
            async cancellationToken =>
                await sampler.FirstIntervalSampleTaken.WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(10));

        await Task.WhenAll(sampler.StopAsync(), sampler.StopAsync());

        Assert.True(sampler.IntervalSamplesTaken > 0);
        Assert.True(sampler.PeakWorkingSetBytes > 0);
        Assert.True(sampler.PeakManagedHeapBytes > 0);
    }
}
