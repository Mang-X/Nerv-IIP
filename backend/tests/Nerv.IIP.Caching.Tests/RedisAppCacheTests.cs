using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Caching;
using Nerv.IIP.Testing;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Nerv.IIP.Caching.Tests;

// #2140 approved spec: real Redis, normal DI, independent application caches; no fake provider.
[Trait("Contract", "ProviderBehavior")]
[Trait("Contract", "Regression")]
public sealed class RedisAppCacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private static readonly EventuallyOptions Observation = new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20), []);

    [RealRedisCacheFact]
    public async Task Redis_configuration_shares_serialized_values_with_existing_and_new_instances()
    {
        await using var run = await RedisRun.CreateAsync();
        using var a = run.Build();
        using var b = run.Build(explicitProvider: true);
        _ = Cache(a);
        _ = Cache(b);
        await run.AssertSubscriptionsAsync(2);
        var expected = new PermissionValue(true, ["read", "view"], 42);
        Assert.Equal(expected, await Cache(a).GetOrCreateAsync("value", () => Task.FromResult(expected), Ttl));
        var received = await Cache(b).GetOrCreateAsync<PermissionValue>("value", () => throw new CacheMissException(), Ttl);
        Assert.True(received.Allowed);
        Assert.Equal(new[] { "read", "view" }, received.Permissions);
        Assert.Equal(42, received.Revision);
        using var newlyStarted = run.Build();
        Assert.Equal(42, (await Cache(newlyStarted).GetOrCreateAsync<PermissionValue>("value", () => throw new CacheMissException(), Ttl)).Revision);
        await run.Database.HashSetAsync(run.RedisKey("gateway", run.Environment, "value"), "data", "{invalid-json");
        using var corruptedReader = run.Build();
        await Assert.ThrowsAsync<FusionCacheSerializationException>(() => Cache(corruptedReader).GetOrCreateAsync(
            "value", () => Task.FromResult(new PermissionValue(false, [], 0)), Ttl));
    }

    [RealRedisCacheFact]
    public async Task Tag_and_clear_propagate_after_subscription_without_touching_other_tenants_services_or_environments()
    {
        await using var run = await RedisRun.CreateAsync();
        using var a = run.Build();
        using var b = run.Build();
        using var otherService = run.Build("other");
        using var otherEnvironment = run.Build(environment: run.Environment + "-other");
        foreach (var instance in new[] { a, b, otherService, otherEnvironment })
        {
            Assert.Equal(1, await Cache(instance).GetOrCreateAsync("tenant-a", () => Task.FromResult(1), Ttl, "tenant:a"));
            Assert.Equal(2, await Cache(instance).GetOrCreateAsync("tenant-b", () => Task.FromResult(2), Ttl, "tenant:b"));
        }
        await run.AssertSubscriptionsAsync(2);
        Cache(a).RemoveByTag("tenant:a");
        await Eventually.AssertAsync("remote tagged L1 becomes invalid", async _ =>
            Assert.Equal(3, await Cache(b).GetOrCreateAsync("tenant-a", () => Task.FromResult(3), Ttl, "tenant:a")), Observation);
        Assert.Equal(2, await Cache(b).GetOrCreateAsync<int>("tenant-b", () => throw new CacheMissException(), Ttl, "tenant:b"));
        Cache(a).Clear();
        await Eventually.AssertAsync("remote clear marker becomes visible", async _ =>
            Assert.Equal(4, await Cache(b).GetOrCreateAsync("tenant-b", () => Task.FromResult(4), Ttl, "tenant:b")), Observation);
        foreach (var instance in new[] { otherService, otherEnvironment })
        {
            Assert.Equal(1, await Cache(instance).GetOrCreateAsync<int>("tenant-a", () => throw new CacheMissException(), Ttl, "tenant:a"));
            Assert.Equal(2, await Cache(instance).GetOrCreateAsync<int>("tenant-b", () => throw new CacheMissException(), Ttl, "tenant:b"));
        }
        // Fresh L1s also read their own L2 tag/clear metadata, proving isolation beyond local hits.
        using var freshOther = run.Build("other");
        using var freshEnvironment = run.Build(environment: run.Environment + "-other");
        foreach (var instance in new[] { freshOther, freshEnvironment })
            Assert.Equal(1, await Cache(instance).GetOrCreateAsync<int>("tenant-a", () => throw new CacheMissException(), Ttl, "tenant:a"));
    }

    [RealRedisCacheFact]
    public async Task L2_promotion_does_not_extend_original_authorization_lifetime()
    {
        await using var run = await RedisRun.CreateAsync();
        using var a = run.Build();
        using var b = run.Build();
        var ttl = TimeSpan.FromSeconds(2);
        Assert.True(await Cache(a).GetOrCreateAsync("allow", () => Task.FromResult(true), ttl));
        var lifetime = Stopwatch.StartNew();
        var key = run.RedisKey("gateway", run.Environment, "allow");
        await Eventually.AssertAsync("real Redis TTL reaches its second half", async _ =>
            Assert.InRange((long)await run.Database.ExecuteAsync("PTTL", key), 1, 1000), Observation);
        Assert.True(await Cache(b).GetOrCreateAsync<bool>("allow", () => throw new CacheMissException(), TimeSpan.FromMinutes(20)));
        // RedisCache's physical expiry is rounded to seconds; wait for the original logical lifetime too.
        await Eventually.AssertAsync("original business lifetime and Redis TTL expire", async _ =>
        {
            Assert.True(lifetime.Elapsed >= ttl);
            Assert.Equal(-2L, (long)await run.Database.ExecuteAsync("PTTL", key));
        }, Observation);
        foreach (var instance in new[] { a, b })
            await Assert.ThrowsAsync<CacheMissException>(() => Cache(instance).GetOrCreateAsync<bool>("allow", () => throw new CacheMissException(), TimeSpan.FromMinutes(20)));
    }

    [RealRedisCacheFact]
    public async Task Redis_read_write_and_backplane_faults_propagate_on_every_attempt_without_stale_authorization()
    {
        await using var run = await RedisRun.CreateAsync();
        var user = await run.CreateUserAsync();
        using var instance = run.Build(user: user);
        var cache = Cache(instance);
        Assert.True(await cache.GetOrCreateAsync("allow", () => Task.FromResult(true), TimeSpan.FromMilliseconds(100)));
        await Eventually.AssertAsync("allow expires in real Redis and local cache", async _ =>
        {
            Assert.Equal(-2L, (long)await run.Database.ExecuteAsync("PTTL", run.RedisKey("gateway", run.Environment, "allow")));
            await Assert.ThrowsAsync<CacheMissException>(() => cache.GetOrCreateAsync<bool>("allow", () => throw new CacheMissException(), Ttl));
        }, Observation);
        await run.SetUserCommandsAsync(user, "-@read");
        for (var attempt = 0; attempt < 2; attempt++)
            await Assert.ThrowsAsync<FusionCacheDistributedCacheException>(() => cache.GetOrCreateAsync("allow", () => Task.FromResult(false), Ttl));
        await run.SetUserCommandsAsync(user, "+@read", "-@write");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<FusionCacheDistributedCacheException>(() => cache.GetOrCreateAsync($"write-{attempt}", () => Task.FromResult(42), Ttl));
            Assert.Throws<FusionCacheDistributedCacheException>(() => cache.RemoveByTag("tenant:a"));
            Assert.Throws<FusionCacheDistributedCacheException>(cache.Clear);
        }
        await run.SetUserCommandsAsync(user, "+@write", "-publish");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<FusionCacheBackplaneException>(() => cache.RemoveByTag("tenant:a"));
            Assert.Throws<FusionCacheBackplaneException>(cache.Clear);
            await Assert.ThrowsAsync<FusionCacheBackplaneException>(() => cache.GetOrCreateAsync($"publish-{attempt}", () => Task.FromResult(42), Ttl));
        }
    }

    [RealRedisCacheFact]
    public async Task Host_start_fails_when_real_backplane_subscription_is_denied()
    {
        await using var run = await RedisRun.CreateAsync();
        var user = await run.CreateUserAsync();
        await run.SetUserCommandsAsync(user, "-subscribe", "-psubscribe");
        using var host = new HostBuilder().ConfigureServices(services => services.AddNervIipCaching(run.Configuration(user: user), "gateway")).Build();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.DoesNotContain("cache-test-secret", error.ToString());
    }

    private static IAppCache Cache(ServiceProvider instance) => instance.GetRequiredService<IAppCache>();
    public sealed record PermissionValue(bool Allowed, string[] Permissions, int Revision);
    private sealed class CacheMissException : Exception;

    private sealed class RedisRun : IAsyncDisposable
    {
        private readonly ConnectionMultiplexer _admin;
        private readonly HashSet<RedisKey> _keys = [];
        private readonly List<string> _users = [];
        public string Environment { get; } = "i2140-" + Guid.NewGuid().ToString("N");
        public IDatabase Database => _admin.GetDatabase();
        private RedisRun(ConnectionMultiplexer admin) => _admin = admin;
        public static async Task<RedisRun> CreateAsync()
        {
            var options = ConfigurationOptions.Parse(System.Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!);
            options.AllowAdmin = true;
            return new RedisRun(await ConnectionMultiplexer.ConnectAsync(options));
        }
        public IConfiguration Configuration(string service = "gateway", string? environment = null, bool explicitProvider = false, string? user = null)
        {
            environment ??= Environment;
            // Exact cleanup inventory for the pinned library's business and internal marker keys; no SCAN/FLUSH.
            foreach (var key in new[] { "value", "tenant-a", "tenant-b", "allow", "write-0", "write-1", "publish-0", "publish-1", "__fc:t:!", "__fc:t:*", "__fc:t:tenant:a", "__fc:t:tenant:b" })
                _keys.Add(RedisKey(service, environment, key));
            var options = ConfigurationOptions.Parse(System.Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!);
            if (user is not null) { options.User = user; options.Password = "cache-test-secret"; }
            return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:Redis"] = options.ToString(includePassword: true),
                ["Caching:Provider"] = explicitProvider ? "Redis" : null,
                ["Caching:Environment"] = environment
            }).Build();
        }
        public ServiceProvider Build(string service = "gateway", string? environment = null, bool explicitProvider = false, string? user = null) =>
            new ServiceCollection().AddNervIipCaching(Configuration(service, environment, explicitProvider, user), service).BuildServiceProvider();
        public string RedisKey(string service, string environment, string key) =>
            $"nerv-iip:{Uri.EscapeDataString(service)}:{Uri.EscapeDataString(environment)}:json-v1:{key}:{FusionCacheOptions.DistributedCacheWireFormatVersion}";
        public async Task AssertSubscriptionsAsync(long expected)
        {
            var channel = $"nerv-iip:gateway:{Environment}:json-v1.Backplane:{FusionCacheOptions.BackplaneWireFormatVersion}";
            var result = (RedisResult[])(await Database.ExecuteAsync("PUBSUB", "NUMSUB", channel))!;
            Assert.Equal(expected, (long)result[1]);
        }
        public async Task<string> CreateUserAsync()
        {
            var user = Environment + "-" + _users.Count;
            _users.Add(user);
            await Database.ExecuteAsync("ACL", "SETUSER", user, "on", ">cache-test-secret", "+@all", $"~nerv-iip:*:{Environment}:*", $"&nerv-iip:*:{Environment}:*");
            return user;
        }
        public Task<RedisResult> SetUserCommandsAsync(string user, params string[] commands) =>
            Database.ExecuteAsync("ACL", new object[] { "SETUSER", user }.Concat(commands.Cast<object>()).ToArray());
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var user in _users) await Database.ExecuteAsync("ACL", "DELUSER", user);
                if (_keys.Count != 0) await Database.KeyDeleteAsync(_keys.ToArray());
                Assert.All(await Task.WhenAll(_keys.Select(key => Database.KeyExistsAsync(key))), exists => Assert.False(exists));
            }
            finally { await _admin.DisposeAsync(); }
        }
    }
}

public sealed class RealRedisCacheFactAttribute : FactAttribute
{
    public RealRedisCacheFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_REDIS to run real Redis FusionCache provider tests.";
    }
}
