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
        await Task.Delay(30);

        await Task.WhenAll(sampler.StopAsync(), sampler.StopAsync());

        Assert.True(sampler.PeakWorkingSetBytes > 0);
        Assert.True(sampler.PeakManagedHeapBytes > 0);
    }
}
