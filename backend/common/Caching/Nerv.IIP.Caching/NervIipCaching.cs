using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

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

    public FusionAppCache(int maxEntries = DefaultMaxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
        _cache = new FusionCache(Options.Create(new FusionCacheOptions()), _memory);
    }

    public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, string? tag = null) =>
        _cache.GetOrSetAsync<T>(key, (_, _) => factory(), options: new FusionCacheEntryOptions
        {
            Duration = ttl,
            Size = 1,
            IsFailSafeEnabled = false,
            EagerRefreshThreshold = null,
            AllowTimedOutFactoryBackgroundCompletion = false,
        }, tags: tag is null ? null : [tag]).AsTask();

    // Includes FusionCache metadata; the capacity budget counts Size units, not raw entry count.
    public int L1EntryCount => _memory.Count;

    public void RemoveByTag(string tag) => _cache.RemoveByTag(tag);
    public void Clear() => _cache.Clear(allowFailSafe: false);

    public void Dispose()
    {
        _cache.Dispose();
        _memory.Dispose();
    }
}

public static class NervIipCachingRegistration
{
    public static IServiceCollection AddNervIipCaching(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddMemoryCache();
        services.AddSingleton<IAppCache>(_ => new FusionAppCache(
            configuration.GetValue("Caching:L1MaxEntries", FusionAppCache.DefaultMaxEntries)));
        services.AddSingleton(new NervIipCacheOptions(serviceName, configuration.GetValue("Caching:Redis", string.Empty) ?? string.Empty));
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
