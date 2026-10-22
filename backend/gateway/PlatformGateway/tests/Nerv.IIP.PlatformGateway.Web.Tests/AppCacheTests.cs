using Nerv.IIP.Caching;
using Nerv.IIP.Testing;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

// #2109 approved spec: successful cold loads merge; unrelated keys remain independent.
[Trait("Contract", "ProviderBehavior")]
[Trait("Contract", "Regression")]
public sealed class AppCacheTests
{
    [Fact]
    public async Task Same_key_successful_cold_loads_share_one_factory()
    {
        using var cache = new FusionAppCache();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<int> Load() { Interlocked.Increment(ref calls); return release.Task; }
        var requests = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrCreateAsync("same", Load, TimeSpan.FromMinutes(1))).ToArray();
        release.SetResult(42);
        var values = await WithBudget(Task.WhenAll(requests));
        Assert.All(values, value => Assert.Equal(42, value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Different_keys_load_without_waiting_for_blocked_key()
    {
        using var cache = new FusionAppCache();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = cache.GetOrCreateAsync("blocked", () => release.Task, TimeSpan.FromMinutes(1));
        try
        {
            var independent = await WithBudget(cache.GetOrCreateAsync("independent", () => Task.FromResult(7), TimeSpan.FromMinutes(1)));
            Assert.Equal(7, independent);
            Assert.False(blocked.IsCompleted);
        }
        finally { release.TrySetResult(42); }
        Assert.Equal(42, await WithBudget(blocked));
    }

    [Fact]
    public async Task Failed_loads_never_become_success_and_later_load_can_succeed()
    {
        using var cache = new FusionAppCache();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 8).Select(_ =>
            cache.GetOrCreateAsync("failure", () => release.Task, TimeSpan.FromMinutes(1))).ToArray();
        release.SetException(new HttpRequestException("source failed"));
        foreach (var request in requests)
            await Assert.ThrowsAsync<HttpRequestException>(() => WithBudget(request));
        Assert.Equal(42, await cache.GetOrCreateAsync("failure", () => Task.FromResult(42), TimeSpan.FromMinutes(1)));
        Assert.Equal(42, await cache.GetOrCreateAsync<int>("failure", () => throw new InvalidOperationException("must hit"), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Capacity_bounds_admission_while_delivering_every_factory_result()
    {
        using var cache = new FusionAppCache(maxEntries: 4);
        for (var i = 0; i < 32; i++)
            Assert.Equal(i, await cache.GetOrCreateAsync($"key-{i}", () => Task.FromResult(i), TimeSpan.FromMinutes(1)));
        Assert.InRange(cache.L1EntryCount, 0, 4);
        var hits = 0;
        for (var i = 0; i < 32; i++)
        {
            try
            {
                Assert.Equal(i, await cache.GetOrCreateAsync<int>($"key-{i}", () => throw new CacheMissException(), TimeSpan.FromMinutes(1)));
                hits++;
            }
            catch (CacheMissException) { }
        }
        Assert.InRange(hits, 0, 4);
    }

    [Fact]
    public async Task Real_library_ttl_expires_without_returning_stale_value_on_source_failure()
    {
        using var cache = new FusionAppCache();
        var ttl = TimeSpan.FromMilliseconds(100);
        Assert.Equal(42, await cache.GetOrCreateAsync("ttl", () => Task.FromResult(42), ttl));
        Assert.Equal(42, await cache.GetOrCreateAsync<int>("ttl", () => throw new CacheMissException(), ttl));
        await Eventually.AssertAsync("FusionCache TTL expires", async _ =>
            await Assert.ThrowsAsync<CacheMissException>(() => cache.GetOrCreateAsync<int>("ttl", () => throw new CacheMissException(), ttl)),
            new EventuallyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), []));
        Assert.Equal(7, await cache.GetOrCreateAsync("ttl", () => Task.FromResult(7), ttl));
    }

    [Fact]
    public async Task Clear_reloads_all_families()
    {
        using var cache = new FusionAppCache();
        await cache.GetOrCreateAsync("gateway:key", () => Task.FromResult(1), TimeSpan.FromMinutes(1), NervIipCacheTags.Gateway);
        await cache.GetOrCreateAsync("iam:key", () => Task.FromResult(2), TimeSpan.FromMinutes(1));
        cache.Clear();
        Assert.Equal(3, await cache.GetOrCreateAsync("gateway:key", () => Task.FromResult(3), TimeSpan.FromMinutes(1), NervIipCacheTags.Gateway));
        Assert.Equal(4, await cache.GetOrCreateAsync("iam:key", () => Task.FromResult(4), TimeSpan.FromMinutes(1)));
    }

    private static async Task<T> WithBudget<T>(Task<T> task) =>
        await TestTimeout.RunAsync("cache load completes", token => new ValueTask<T>(task.WaitAsync(token)), TimeSpan.FromSeconds(10));

    private sealed class CacheMissException : Exception;
}
