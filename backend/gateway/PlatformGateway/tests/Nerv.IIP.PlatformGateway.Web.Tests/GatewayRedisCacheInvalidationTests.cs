using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.Resilience;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

// #4172 approved r2: unchanged HTTP entry, real Redis and two independently hosted gateways.
[Trait("Contract", "ProviderBehavior")]
[Trait("Contract", "Regression")]
[Collection("Gateway Redis HTTP invalidation")]
public sealed class GatewayRedisCacheInvalidationTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private static readonly EventuallyOptions Observation = new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20), []);

    [RealGatewayRedisFact]
    public async Task Existing_HTTP_invalidation_refreshes_all_gateway_families_and_preserves_other_services()
    {
        await using var run = await RedisRun.CreateAsync();
        var source = new Authority();
        await using var a = run.Gateway(source);
        await using var b = run.Gateway(source);
        using var clientA = a.CreateClient();
        using var clientB = b.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        clientB.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        var cacheB = b.Services.GetRequiredService<IAppCache>();
        await run.AssertSubscriptionsAsync(2);
        using var otherService = run.OtherService();
        var otherCache = otherService.GetRequiredService<IAppCache>();
        Assert.Equal(7, await otherCache.GetOrCreateAsync("control", () => Task.FromResult(7), Lifetime, "gateway"));
        Assert.Equal(1, await cacheB.GetOrCreateAsync("edge", () => Task.FromResult(1), Lifetime, "gateway"));
        foreach (var client in new[] { clientA, clientB })
        {
            Assert.Equal("old", await ReadNameAsync(client, detail: false));
            Assert.Equal("old", await ReadNameAsync(client, detail: true));
        }
        source.Name = "new";
        // Real authentication/policy rejection must leave both cached families untouched.
        foreach (var token in new string?[] { null, "wrong-internal-identity", GatewayTestTokens.ValidAccessToken() })
        {
            using var rejected = await InvalidateAsync(clientA, token);
            Assert.Contains(rejected.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
            Assert.Equal(1, await cacheB.GetOrCreateAsync<int>("edge", () => throw new CacheMissException(), Lifetime, "gateway"));
            Assert.Equal("old", await ReadNameAsync(clientB, detail: false));
            Assert.Equal("old", await ReadNameAsync(clientB, detail: true));
        }
        using var accepted = await InvalidateAsync(clientA, InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        // This long-lived L1 sentinel is an observable B processing edge, not a PUBSUB delivery guess
        // or a five-second endpoint TTL expiry masquerading as notification propagation.
        await AwaitInvalidationEdgeAsync(cacheB);
        Assert.Equal("new", await ReadNameAsync(clientB, detail: false));
        Assert.Equal("new", await ReadNameAsync(clientB, detail: true));
        source.Allowed = false;
        using var revoke = await InvalidateAsync(clientA, InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        await AwaitInvalidationEdgeAsync(cacheB, 3);
        using var denied = await ReadAsync(clientB, detail: true);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(7, await otherCache.GetOrCreateAsync<int>("control", () => throw new CacheMissException(), Lifetime, "gateway"));
        using var freshOther = run.OtherService();
        Assert.Equal(7, await freshOther.GetRequiredService<IAppCache>().GetOrCreateAsync<int>("control", () => throw new CacheMissException(), Lifetime, "gateway"));
    }

    [RealGatewayRedisFact]
    public async Task In_flight_old_factory_may_return_old_value_but_subsequent_HTTP_read_reloads_authority()
    {
        await using var run = await RedisRun.CreateAsync();
        var source = new Authority { BlockDetail = true };
        await using var a = run.Gateway(source);
        await using var b = run.Gateway(source);
        using var clientA = a.CreateClient();
        using var clientB = b.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        clientB.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        var cacheB = b.Services.GetRequiredService<IAppCache>();
        await run.AssertSubscriptionsAsync(2);
        Assert.Equal(1, await cacheB.GetOrCreateAsync("edge", () => Task.FromResult(1), Lifetime, "gateway"));
        var pending = ReadNameAsync(clientB, detail: true);
        await TestTimeout.RunAsync("old authoritative factory captured its response", async token =>
            await source.Captured.Task.WaitAsync(token), TimeSpan.FromSeconds(10));
        try
        {
            source.Name = "new";
            using var accepted = await InvalidateAsync(clientA, InternalServiceAuthentication.DefaultDevelopmentBearerToken);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            await AwaitInvalidationEdgeAsync(cacheB);
        }
        finally { source.Release.TrySetResult(); }
        Assert.Equal("old", await pending);
        Assert.Equal("new", await ReadNameAsync(clientB, detail: true));
        Assert.Equal("new", await ReadNameAsync(clientA, detail: true));
    }

    [RealGatewayRedisFact]
    public async Task Redis_write_and_notification_failures_return_failure_without_sensitive_logs_or_fake_204()
    {
        await using var run = await RedisRun.CreateAsync();
        var source = new Authority();
        var logs = new CapturedLogs();
        var user = await run.CreateUserAsync();
        await using var a = run.Gateway(source, user, logs);
        using var client = a.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        Assert.Equal("old", await ReadNameAsync(client, detail: true));
        foreach (var commands in new[] { new[] { "-@write" }, new[] { "+@write", "-publish" } })
        {
            await run.SetUserCommandsAsync(user, commands);
            using var failed = await InvalidateAsync(client, InternalServiceAuthentication.DefaultDevelopmentBearerToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.DoesNotContain("cache-test-secret", await failed.Content.ReadAsStringAsync());
        }
        Assert.Contains(logs.Messages, message => message.Contains("remove-by-tag", StringComparison.Ordinal));
        Assert.All(logs.Messages, message =>
        {
            Assert.DoesNotContain("cache-test-secret", message);
            Assert.DoesNotContain(user, message);
            Assert.DoesNotContain(run.Identity("platform-gateway"), message);
        });
    }

    private static ValueTask AwaitInvalidationEdgeAsync(IAppCache cache, int next = 2) =>
        Eventually.AssertAsync("B processed gateway tag invalidation", async _ =>
            Assert.Equal(next, await cache.GetOrCreateAsync("edge", () => Task.FromResult(next), Lifetime, "gateway")), Observation);

    private static async Task<HttpResponseMessage> InvalidateAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/gateway/cache/invalidate");
        request.Headers.Authorization = new("Bearer", token ?? string.Empty);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ReadAsync(HttpClient client, bool detail) =>
        client.GetAsync("/api/console/v1/instances" + (detail ? "/demo" : "") + "?organizationId=org-001&environmentId=env-dev");

    private static async Task<string> ReadNameAsync(HttpClient client, bool detail)
    {
        using var response = await ReadAsync(client, detail);
        response.EnsureSuccessStatusCode();
        if (detail)
            return (await response.Content.ReadFromJsonAsync<Envelope<InstanceDetailResponse>>())!.Data.ApplicationName;
        return (await response.Content.ReadFromJsonAsync<Envelope<InstanceListResponse>>())!.Data.Items.Single().ApplicationName;
    }

    private sealed record Envelope<T>(T Data, bool Success = true, string Message = "OK", int Code = 0);
    private sealed class CacheMissException : Exception;

    private sealed class Authority : IAppHubClient
    {
        public string Name { get; set; } = "old";
        public bool Allowed { get; set; } = true;
        public bool BlockDetail { get; set; }
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<InstanceListResponse> QueryInstancesAsync(InstanceListQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new InstanceListResponse(query.PageIndex, query.PageSize, 1,
                [new("app", Name, "1.0", "node", "docker", "demo", "demo", "running", "healthy", null, null)]));
        public async Task<InstanceDetailResponse> GetInstanceAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken)
        {
            var response = new InstanceDetailResponse("app", Name, "1.0", "node", "docker", instanceKey, "demo", "running", "healthy", null, null, [], new Dictionary<string, string>());
            if (BlockDetail)
            {
                BlockDetail = false;
                Captured.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return response;
        }
    }

    private sealed class IamAuthorityHandler(Authority source) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Envelope<AuthorizationCheckResponse>(
                    new(source.Allowed, "user-admin", "user", "admin", source.Allowed ? null : "revoked")))
            });
    }

    private sealed class RedisRun(ConnectionMultiplexer admin) : IAsyncDisposable
    {
        private readonly HashSet<RedisKey> _keys = [];
        private readonly List<string> _users = [];
        private readonly string _environment = "i4172-" + Guid.NewGuid().ToString("N");
        private IDatabase Database => admin.GetDatabase();
        public static async Task<RedisRun> CreateAsync()
        {
            var options = ConfigurationOptions.Parse(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!);
            options.AllowAdmin = true;
            return new(await ConnectionMultiplexer.ConnectAsync(options));
        }
        public string Identity(string service) => $"nerv-iip:{service}:{_environment}:json-v1";
        private IConfiguration Configuration(string service, string? user = null)
        {
            var options = ConfigurationOptions.Parse(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!);
            if (user is not null) { options.User = user; options.Password = "cache-test-secret"; }
            foreach (var key in new[] { "control", "edge", "__fc:t:!", "__fc:t:*", "__fc:t:gateway",
                NervIipCacheKeys.GatewayInstanceDetail("org-001", "env-dev", "demo"),
                NervIipCacheKeys.GatewayInstanceList("org-001", "env-dev", NervIipCacheKeys.HashQuery(new InstanceListQuery("org-001", "env-dev", 1, 20, null, null, null))) })
                Track(service, key);
            // The production authorization key is observed via the exact known token/requirements;
            // key inventory remains finite and never scans shared Redis.
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GatewayTestTokens.ValidAccessToken()))).ToLowerInvariant();
            foreach (var resource in new[] { "-", "demo" })
                Track(service, $"gateway:authorization:{hash}:permission-version:7:apphub.instances.read:org-001:env-dev:application-instance:{resource}:v1");
            return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:Redis"] = options.ToString(includePassword: true),
                ["Caching:Environment"] = _environment,
                ["Gateway:AuthorizationCacheTtlSeconds"] = "120"
            }).Build();
        }
        private void Track(string service, string key) => _keys.Add($"{Identity(service)}:{key}:{FusionCacheOptions.DistributedCacheWireFormatVersion}");
        public WebApplicationFactory<Program> Gateway(Authority source, string? user = null, CapturedLogs? logs = null)
        {
            var config = Configuration("platform-gateway", user);
            var factory = new GatewayFactory(config, source, logs);
            factory.UseKestrel(0);
            return factory;
        }
        public ServiceProvider OtherService() => new ServiceCollection().AddNervIipCaching(Configuration("other-service"), "other-service").BuildServiceProvider();
        public async Task AssertSubscriptionsAsync(long expected)
        {
            var channel = $"{Identity("platform-gateway")}.Backplane:{FusionCacheOptions.BackplaneWireFormatVersion}";
            var result = (RedisResult[])(await Database.ExecuteAsync("PUBSUB", "NUMSUB", channel))!;
            Assert.Equal(expected, (long)result[1]);
        }
        public async Task<string> CreateUserAsync()
        {
            var user = _environment + "-gateway";
            _users.Add(user);
            await Database.ExecuteAsync("ACL", "SETUSER", user, "on", ">cache-test-secret", "+@all", $"~{Identity("platform-gateway")}:*", $"&{Identity("platform-gateway")}*");
            return user;
        }
        public Task<RedisResult> SetUserCommandsAsync(string user, string[] commands) =>
            Database.ExecuteAsync("ACL", new object[] { "SETUSER", user }.Concat(commands.Cast<object>()).ToArray());
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var user in _users) await Database.ExecuteAsync("ACL", "DELUSER", user);
                await Database.KeyDeleteAsync(_keys.ToArray());
                Assert.All(await Task.WhenAll(_keys.Select(key => Database.KeyExistsAsync(key))), exists => Assert.False(exists));
            }
            finally { await admin.DisposeAsync(); }
        }
    }

    // Keep Kestrel configuration and CreateHost on the same factory: WithWebHostBuilder delegates
    // CreateHost/ConfigureClient to its parent, which cannot own the child's Kestrel server.
    private sealed class GatewayFactory(IConfiguration config, Authority source, CapturedLogs? logs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(config));
            builder.ConfigureServices(services =>
            {
                services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, PlatformGatewayTestHostGate.RequestPermitStartupFilter>());
                services.RemoveAll<IAppCache>();
                services.AddNervIipCaching(config, "platform-gateway");
                services.Configure<GatewayAuthorizationOptions>(config.GetSection("Gateway"));
                services.RemoveAll<IAppHubClient>();
                services.AddSingleton<IAppHubClient>(source);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddTransient<IGatewayAuthorizationClient>(sp => new HttpGatewayAuthorizationClient(
                    new HttpClient(new IamAuthorityHandler(source)) { BaseAddress = new Uri("http://iam.test") },
                    sp.GetRequiredService<IAppCache>(), sp.GetRequiredService<IOptions<GatewayAuthorizationOptions>>(),
                    sp.GetRequiredService<GatewayDownstreamHealthState>()));
                if (logs is not null) services.AddLogging(logging => logging.AddProvider(logs));
            });
        }
        protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder) =>
            PlatformGatewayTestHostGate.Build(() => base.CreateHost(builder));
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(Messages);
        public void Dispose() { }
        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) messages.Enqueue(formatter(state, exception) + exception?.ToString());
            }
        }
    }
}

// The in-flight factory test requires A to serve an invalidation while B holds a request.
// A concurrent unrelated host build drains the shared host gate and prevents that controlled
// interleaving. Isolate only this collection; the two gateways still run concurrently within it.
[CollectionDefinition("Gateway Redis HTTP invalidation", DisableParallelization = true)]
public sealed class GatewayRedisHttpInvalidationCollection;

public sealed class RealGatewayRedisFactAttribute : FactAttribute
{
    public RealGatewayRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_REDIS to run real Redis dual PlatformGateway HTTP invalidation tests.";
    }
}
