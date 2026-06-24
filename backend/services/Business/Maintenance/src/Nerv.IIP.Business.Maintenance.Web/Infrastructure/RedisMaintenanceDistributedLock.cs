using System.Security.Cryptography;
using NetCorePal.Extensions.DistributedLocks;
using StackExchange.Redis;

namespace Nerv.IIP.Business.Maintenance.Web.Infrastructure;

public sealed class RedisMaintenanceDistributedLock(IRedisCommandLockStore store, TimeProvider timeProvider) : IDistributedLock
{
    private static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultLeaseTime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    public ILockSynchronizationHandler? TryAcquire(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return TryAcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public ILockSynchronizationHandler Acquire(string key, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return AcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask<ILockSynchronizationHandler?> TryAcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadlineUtc = timeProvider.GetUtcNow().Add(timeout);
        while (true)
        {
            var token = NewToken();
            if (await store.TryAcquireAsync(key, token, DefaultLeaseTime, cancellationToken))
            {
                return new Handle(store, key, token);
            }

            if (timeout <= TimeSpan.Zero || timeProvider.GetUtcNow() >= deadlineUtc)
            {
                return null!;
            }

            var remaining = deadlineUtc - timeProvider.GetUtcNow();
            var delay = remaining < RetryDelay ? remaining : RetryDelay;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public async ValueTask<ILockSynchronizationHandler> AcquireAsync(string key, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var handle = await TryAcquireAsync(key, timeout ?? DefaultAcquireTimeout, cancellationToken);
        if (handle is null)
        {
            throw new TimeoutException($"Could not acquire distributed lock '{key}'.");
        }

        return handle;
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    private sealed class Handle(IRedisCommandLockStore store, string key, string token) : ILockSynchronizationHandler
    {
        private int disposed;

        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }

            await store.ReleaseAsync(key, token, CancellationToken.None);
        }
    }
}

public interface IRedisCommandLockStore
{
    Task<bool> TryAcquireAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken);

    Task ReleaseAsync(string key, string token, CancellationToken cancellationToken);
}

public sealed class StackExchangeRedisCommandLockStore(IDatabase database) : IRedisCommandLockStore
{
    private const string KeyPrefix = "nerv-iip:business-maintenance:locks:";
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end
        return 0
        """;

    public async Task<bool> TryAcquireAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return await database.StringSetAsync(ToRedisKey(key), token, leaseTime, When.NotExists);
    }

    public async Task ReleaseAsync(string key, string token, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await database.ScriptEvaluateAsync(ReleaseScript, [ToRedisKey(key)], [(RedisValue)token]);
    }

    private static RedisKey ToRedisKey(string key) => KeyPrefix + key;
}

public sealed class InMemoryRedisCommandLockStore : IRedisCommandLockStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<string, LockEntry> locks = new(StringComparer.Ordinal);

    public Task<bool> TryAcquireAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            if (locks.TryGetValue(key, out var current) && current.ExpiresAtUtc > now)
            {
                return Task.FromResult(false);
            }

            locks[key] = new LockEntry(token, now.Add(leaseTime));
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(string key, string token, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (syncRoot)
        {
            if (locks.TryGetValue(key, out var current) && string.Equals(current.Token, token, StringComparison.Ordinal))
            {
                locks.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private sealed record LockEntry(string Token, DateTimeOffset ExpiresAtUtc);
}
