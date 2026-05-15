using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Caching;

public interface IAppCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl);
    void InvalidatePrefix(string prefix);
    void Clear();
}

public sealed class MemoryAppCache : IAppCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        if (_entries.TryGetValue(key, out var existing) && existing.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return (T)existing.Value;
        }

        var value = await factory();
        _entries[key] = new CacheEntry(value!, DateTimeOffset.UtcNow.Add(ttl));
        return value;
    }

    public void InvalidatePrefix(string prefix)
    {
        foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _entries.TryRemove(key, out _);
        }
    }

    public void Clear() => _entries.Clear();

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAtUtc);
}

public static class NervIipCachingRegistration
{
    public static IServiceCollection AddNervIipCaching(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddMemoryCache();
        services.AddSingleton<IAppCache, MemoryAppCache>();
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
