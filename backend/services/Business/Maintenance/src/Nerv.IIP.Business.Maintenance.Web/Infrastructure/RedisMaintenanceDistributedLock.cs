using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetCorePal.Extensions.DistributedLocks;
using StackExchange.Redis;

namespace Nerv.IIP.Business.Maintenance.Web.Infrastructure;

public sealed class RedisMaintenanceDistributedLock : IDistributedLock
{
    private static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultLeaseTime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRenewalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly IRedisCommandLockStore store;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RedisMaintenanceDistributedLock> logger;
    private readonly TimeSpan leaseTime;
    private readonly TimeSpan renewalInterval;

    public RedisMaintenanceDistributedLock(
        IRedisCommandLockStore store,
        TimeProvider timeProvider,
        TimeSpan? leaseTime = null,
        TimeSpan? renewalInterval = null,
        ILogger<RedisMaintenanceDistributedLock>? logger = null)
    {
        this.store = store;
        this.timeProvider = timeProvider;
        this.logger = logger ?? NullLogger<RedisMaintenanceDistributedLock>.Instance;
        this.leaseTime = leaseTime ?? DefaultLeaseTime;
        this.renewalInterval = renewalInterval ?? DefaultRenewalInterval;
        if (this.leaseTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTime), "Lease time must be positive.");
        }

        if (this.renewalInterval <= TimeSpan.Zero || this.renewalInterval >= this.leaseTime)
        {
            throw new ArgumentOutOfRangeException(nameof(renewalInterval), "Renewal interval must be positive and shorter than the lease time.");
        }
    }

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
            if (await store.TryAcquireAsync(key, token, leaseTime, cancellationToken))
            {
                return new Handle(store, timeProvider, logger, key, token, leaseTime, renewalInterval);
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

    private sealed class Handle : ILockSynchronizationHandler
    {
        private readonly IRedisCommandLockStore store;
        private readonly TimeProvider timeProvider;
        private readonly ILogger<RedisMaintenanceDistributedLock> logger;
        private readonly string key;
        private readonly string token;
        private readonly TimeSpan leaseTime;
        private readonly TimeSpan renewalInterval;
        private readonly CancellationTokenSource stopRenewal = new();
        private readonly CancellationTokenSource handleLost = new();
        private readonly Task renewalTask;
        private int disposed;

        public Handle(
            IRedisCommandLockStore store,
            TimeProvider timeProvider,
            ILogger<RedisMaintenanceDistributedLock> logger,
            string key,
            string token,
            TimeSpan leaseTime,
            TimeSpan renewalInterval)
        {
            this.store = store;
            this.timeProvider = timeProvider;
            this.logger = logger;
            this.key = key;
            this.token = token;
            this.leaseTime = leaseTime;
            this.renewalInterval = renewalInterval;
            renewalTask = RenewUntilDisposedAsync();
        }

        public CancellationToken HandleLostToken => handleLost.Token;

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

            await stopRenewal.CancelAsync();
            await renewalTask;
            await store.ReleaseAsync(key, token, CancellationToken.None);
        }

        private async Task RenewUntilDisposedAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(renewalInterval, timeProvider, stopRenewal.Token);
                    if (!await store.RenewAsync(key, token, leaseTime, stopRenewal.Token))
                    {
                        logger.LogWarning(
                            "Distributed lock {LockKey} renewal was rejected; the lock handle will be canceled.",
                            key);
                        await handleLost.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (stopRenewal.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Distributed lock {LockKey} renewal failed; the lock handle will be canceled.",
                    key);
                await handleLost.CancelAsync();
            }
        }
    }
}

public interface IRedisCommandLockStore
{
    Task<bool> TryAcquireAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken);

    Task<bool> RenewAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken);

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
    private const string RenewScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('pexpire', KEYS[1], ARGV[2])
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

    public async Task<bool> RenewAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var leaseMilliseconds = checked((long)Math.Ceiling(leaseTime.TotalMilliseconds));
        var result = await database.ScriptEvaluateAsync(
            RenewScript,
            [ToRedisKey(key)],
            [(RedisValue)token, leaseMilliseconds]);
        return (long)result == 1;
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

    public Task<bool> RenewAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            if (!locks.TryGetValue(key, out var current)
                || current.ExpiresAtUtc <= now
                || !string.Equals(current.Token, token, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            locks[key] = current with { ExpiresAtUtc = now.Add(leaseTime) };
            return Task.FromResult(true);
        }
    }

    private sealed record LockEntry(string Token, DateTimeOffset ExpiresAtUtc);
}
