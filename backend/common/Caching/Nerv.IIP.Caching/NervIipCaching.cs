using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using ZiggyCreatures.Caching.Fusion;
using StackExchange.Redis;

namespace Nerv.IIP.Caching;

public interface IAppCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, string? tag = null);
    void RemoveByTag(string tag);
    void Clear();
}

public static class NervIipCacheTags
{
    public const string Gateway = "gateway";
}

/// <summary>L1 budget is an entry count: each business value costs one unit.
/// FusionCache's fixed tag metadata costs zero units. MemoryCache controls admission/compaction;
/// this is neither byte accounting nor an exact LRU guarantee.</summary>
public sealed class FusionAppCache : IAppCache, IDisposable
{
    public const int DefaultMaxEntries = 10_000;
    private readonly MemoryCache _memory;
    private readonly FusionCache _cache;
    private readonly RedisCache? _distributed;
    private readonly ILogger<FusionAppCache>? _logger;

    public FusionAppCache(int maxEntries = DefaultMaxEntries)
        : this(maxEntries, "FusionCache", null, null) { }

    internal FusionAppCache(int maxEntries, string identity, ConfigurationOptions? redis, ILogger<FusionAppCache>? logger)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _logger = logger;
        _memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
        var options = new FusionCacheOptions
        {
            CacheName = identity,
            CacheKeyPrefix = identity + ":",
            DistributedCacheKeyModifierMode = CacheKeyModifierMode.Suffix,
            WaitForInitialBackplaneSubscribe = true,
            EnableAutoRecovery = false,
            DistributedCacheCircuitBreakerDuration = TimeSpan.Zero,
            BackplaneCircuitBreakerDuration = TimeSpan.Zero,
        };
        ApplyFailurePolicy(options.TagsDefaultEntryOptions);
        _cache = new FusionCache(Options.Create(options), _memory);
        if (redis is null) return;
        _distributed = new RedisCache(Options.Create(new RedisCacheOptions { ConfigurationOptions = redis }));
        try
        {
            _cache.SetupDistributedCache(_distributed, new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var backplane = new SubscriptionCheckedRedisBackplane(new RedisBackplane(new RedisBackplaneOptions { ConfigurationOptions = redis }));
            _cache.SetupBackplane(backplane);
            if (!backplane.IsSubscribed)
                throw new InvalidOperationException("Caching:Redis backplane subscription failed.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, string? tag = null)
    {
        var options = new FusionCacheEntryOptions { Duration = ttl, Size = 1 };
        ApplyFailurePolicy(options);
        var loaded = false;
        try
        {
            var value = await _cache.GetOrSetAsync<T>(key, (_, _) => { loaded = true; return factory(); },
                options: options, tags: tag is null ? null : [tag]);
            _logger?.LogDebug("Cache {Outcome}", loaded ? "miss" : "hit");
            return value;
        }
        catch (Exception error)
        {
            LogError("get", error);
            throw;
        }
    }

    private static void ApplyFailurePolicy(FusionCacheEntryOptions options)
    {
        options.IsFailSafeEnabled = false;
        options.EagerRefreshThreshold = null;
        options.AllowTimedOutFactoryBackgroundCompletion = false;
        options.AllowBackgroundDistributedCacheOperations = false;
        options.AllowBackgroundBackplaneOperations = false;
        options.ReThrowDistributedCacheExceptions = true;
        options.ReThrowSerializationExceptions = true;
        options.ReThrowBackplaneExceptions = true;
    }

    // Includes FusionCache metadata; the capacity budget counts Size units, not raw entry count.
    public int L1EntryCount => _memory.Count;

    public void RemoveByTag(string tag)
    {
        try { _cache.RemoveByTag(tag); }
        catch (Exception error) { LogError("remove-by-tag", error); throw; }
    }

    public void Clear()
    {
        try { _cache.Clear(allowFailSafe: false); }
        catch (Exception error) { LogError("clear", error); throw; }
    }

    // The library's verbose logs include raw keys and provider errors. Log only safe boundary metadata.
    private void LogError(string operation, Exception error) =>
        _logger?.LogWarning("Cache operation {Operation} failed ({ExceptionType})", operation, error.GetType().Name);

    public void Dispose()
    {
        _cache.Dispose();
        _distributed?.Dispose();
        _memory.Dispose();
    }
}

public static class NervIipCachingRegistration
{
    public static IServiceCollection AddNervIipCaching(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var redis = configuration["Caching:Redis"];
        var provider = configuration["Caching:Provider"] ?? (string.IsNullOrWhiteSpace(redis) ? "L1" : "Redis");
        if (provider is not ("L1" or "Redis"))
            throw new InvalidOperationException("Caching:Provider must be L1 or Redis.");
        if (provider == "L1" && !string.IsNullOrWhiteSpace(redis))
            throw new InvalidOperationException("Caching:Provider L1 conflicts with Caching:Redis.");
        var maxEntriesText = configuration["Caching:L1MaxEntries"];
        var maxEntries = FusionAppCache.DefaultMaxEntries;
        if (maxEntriesText is not null && (!int.TryParse(maxEntriesText, out maxEntries) || maxEntries <= 0))
            throw new InvalidOperationException("Caching:L1MaxEntries must be a positive integer.");
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new InvalidOperationException("Caching requires a service name.");
        string? environment = null;
        ConfigurationOptions? redisOptions = null;
        if (provider == "Redis")
        {
            if (string.IsNullOrWhiteSpace(redis))
                throw new InvalidOperationException("Caching:Provider Redis requires Caching:Redis.");
            environment = configuration["Caching:Environment"] ?? configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"];
            if (string.IsNullOrWhiteSpace(environment))
                throw new InvalidOperationException("Caching:Environment or the host environment must be specified for Redis.");
            try
            {
                redisOptions = ConfigurationOptions.Parse(redis);
                if (redisOptions.EndPoints.Count == 0)
                    throw new InvalidOperationException("Caching:Redis requires an endpoint.");
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("Caching:Redis contains invalid connection options.");
            }
        }
        services.AddMemoryCache();
        var identity = $"nerv-iip:{Uri.EscapeDataString(serviceName)}:{Uri.EscapeDataString(environment ?? "L1")}:json-v1";
        services.AddSingleton<IAppCache>(sp => new FusionAppCache(maxEntries, identity, redisOptions, sp.GetService<ILogger<FusionAppCache>>()));
        services.AddHostedService<CachingStartup>();
        services.AddSingleton(new NervIipCacheOptions(serviceName, redis ?? string.Empty));
        return services;
    }
}

public sealed record NervIipCacheOptions(string ServiceName, string RedisConnectionString);

public static class NervIipCacheKeys
{
    public static string AppHubInstanceList(string organizationId, string environmentId, string normalizedQueryHash, int schemaVersion = 1)
        => $"apphub:instance-list:{organizationId}:{environmentId}:query:{normalizedQueryHash}:v{schemaVersion}";

    public static string AppHubInstanceDetail(string organizationId, string environmentId, string instanceKey, int schemaVersion = 1)
        => $"apphub:instance-detail:{organizationId}:{environmentId}:instance:{instanceKey}:v{schemaVersion}";

    public static string GatewayInstanceList(string organizationId, string environmentId, string normalizedQueryHash, int schemaVersion = 1)
        => $"gateway:instance-list:{organizationId}:{environmentId}:query:{normalizedQueryHash}:v{schemaVersion}";

    public static string GatewayInstanceDetail(string organizationId, string environmentId, string instanceKey, int schemaVersion = 1)
        => $"gateway:instance-detail:{organizationId}:{environmentId}:instance:{instanceKey}:v{schemaVersion}";

    public static string IamPermissionSnapshot(string organizationId, string environmentId, string principalId, int schemaVersion = 1)
        => $"iam:permission-snapshot:{organizationId}:{environmentId}:principal:{principalId}:v{schemaVersion}";

    public static string HashQuery<T>(T query)
    {
        var json = JsonSerializer.Serialize(query, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// Resolve the selected provider when the host starts, including the actual backplane subscription.
internal sealed class CachingStartup(IAppCache cache) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) { _ = cache; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
