using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;
using StackExchange.Redis;

namespace Nerv.IIP.DistributedLocking;

public static class NervIipCommandLockingServiceCollectionExtensions
{
    public static IServiceCollection AddNervIipCommandLocking(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool isTesting,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        services.TryAddSingleton(TimeProvider.System);
        if (isTesting)
        {
            services.AddInMemoryDistributedLock();
            return services;
        }

        var redisConnectionString = ResolveRedisConnectionString(configuration);
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            if (environment.IsDevelopment())
            {
                services.AddInMemoryDistributedLock();
                return services;
            }

            throw new InvalidOperationException(
                $"{serviceName} distributed command locks require a Redis connection string outside Development. " +
                "Set ConnectionStrings:Redis, Messaging:Redis:ConnectionString, or Caching:Redis.");
        }

        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IRedisCommandLockStore>(sp => new StackExchangeRedisCommandLockStore(
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase(),
            serviceName));
        services.AddSingleton<IDistributedLock>(sp => new RedisCommandDistributedLock(
            sp.GetRequiredService<IRedisCommandLockStore>(),
            sp.GetRequiredService<TimeProvider>(),
            logger: sp.GetRequiredService<ILogger<RedisCommandDistributedLock>>()));
        return services;
    }

    private static string? ResolveRedisConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("Redis")
        ?? configuration["Messaging:Redis:ConnectionString"]
        ?? configuration["ConnectionStrings:Redis"]
        ?? configuration["Caching:Redis"];
}

public sealed class NervIipCommandLockBehavior<TRequest, TResponse>(
    IEnumerable<ICommandLock<TRequest>> commandLocks,
    IDistributedLock distributedLock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var lockProviders = commandLocks.ToArray();
        if (lockProviders.Length == 0)
        {
            return await next(cancellationToken);
        }

        var handles = new List<ILockSynchronizationHandler>();
        var acquiredKeys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var lockProvider in lockProviders)
            {
                var settings = await lockProvider.GetLockKeysAsync(request, cancellationToken);
                foreach (var key in EnumerateKeys(settings))
                {
                    if (acquiredKeys.Add(key))
                    {
                        handles.Add(await distributedLock.AcquireAsync(key, settings.AcquireTimeout, cancellationToken));
                    }
                }
            }

            if (handles.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Command lock configuration for {typeof(TRequest).Name} did not provide a lock key.");
            }

            var linkedTokens = new CancellationToken[handles.Count + 1];
            linkedTokens[0] = cancellationToken;
            for (var i = 0; i < handles.Count; i++)
            {
                linkedTokens[i + 1] = handles[i].HandleLostToken;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(linkedTokens);
            return await next(linkedCancellation.Token);
        }
        finally
        {
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                await handles[i].DisposeAsync();
            }
        }
    }

    private static IEnumerable<string> EnumerateKeys(CommandLockSettings settings)
    {
        if (settings.LockKeys is not null)
        {
            foreach (var key in settings.LockKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return key;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.LockKey))
        {
            yield return settings.LockKey;
        }
    }
}

public sealed class RedisCommandDistributedLock : IDistributedLock
{
    private static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultLeaseTime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRenewalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly IRedisCommandLockStore store;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RedisCommandDistributedLock> logger;
    private readonly TimeSpan leaseTime;
    private readonly TimeSpan renewalInterval;

    public RedisCommandDistributedLock(
        IRedisCommandLockStore store,
        TimeProvider timeProvider,
        TimeSpan? leaseTime = null,
        TimeSpan? renewalInterval = null,
        ILogger<RedisCommandDistributedLock>? logger = null)
    {
        this.store = store;
        this.timeProvider = timeProvider;
        this.logger = logger ?? NullLogger<RedisCommandDistributedLock>.Instance;
        this.leaseTime = leaseTime ?? DefaultLeaseTime;
        this.renewalInterval = renewalInterval ?? DefaultRenewalInterval;
        if (this.leaseTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTime), "Lease time must be positive.");
        }

        if (this.renewalInterval <= TimeSpan.Zero || this.renewalInterval >= this.leaseTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renewalInterval),
                "Renewal interval must be positive and shorter than the lease time.");
        }
    }

    public ILockSynchronizationHandler? TryAcquire(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        TryAcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

    public ILockSynchronizationHandler Acquire(
        string key,
        TimeSpan? timeout,
        CancellationToken cancellationToken) =>
        AcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

    public async ValueTask<ILockSynchronizationHandler?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken)
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
                return null;
            }

            var remaining = deadlineUtc - timeProvider.GetUtcNow();
            var delay = remaining < RetryDelay ? remaining : RetryDelay;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public async ValueTask<ILockSynchronizationHandler> AcquireAsync(
        string key,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var handle = await TryAcquireAsync(key, timeout ?? DefaultAcquireTimeout, cancellationToken);
        return handle ?? throw new TimeoutException($"Could not acquire distributed lock '{key}'.");
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
        private readonly string key;
        private readonly string token;
        private readonly CancellationTokenSource stopRenewal = new();
        private readonly CancellationTokenSource handleLost = new();
        private readonly Task renewalTask;
        private int disposed;

        public Handle(
            IRedisCommandLockStore store,
            TimeProvider timeProvider,
            ILogger<RedisCommandDistributedLock> logger,
            string key,
            string token,
            TimeSpan leaseTime,
            TimeSpan renewalInterval)
        {
            this.store = store;
            this.key = key;
            this.token = token;
            renewalTask = RenewUntilDisposedAsync(
                store,
                timeProvider,
                logger,
                key,
                token,
                leaseTime,
                renewalInterval,
                stopRenewal,
                handleLost);
        }

        public CancellationToken HandleLostToken => handleLost.Token;

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

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

        private static async Task RenewUntilDisposedAsync(
            IRedisCommandLockStore store,
            TimeProvider timeProvider,
            ILogger<RedisCommandDistributedLock> logger,
            string key,
            string token,
            TimeSpan leaseTime,
            TimeSpan renewalInterval,
            CancellationTokenSource stopRenewal,
            CancellationTokenSource handleLost)
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

public sealed class StackExchangeRedisCommandLockStore(IDatabase database, string serviceName)
    : IRedisCommandLockStore
{
    private readonly string keyPrefix =
        $"nerv-iip:{NormalizeServiceName(serviceName)}:locks:";

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

    public async Task<bool> TryAcquireAsync(
        string key,
        string token,
        TimeSpan leaseTime,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return await database.StringSetAsync(ToRedisKey(key), token, leaseTime, When.NotExists);
    }

    public async Task ReleaseAsync(string key, string token, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await database.ScriptEvaluateAsync(ReleaseScript, [ToRedisKey(key)], [(RedisValue)token]);
    }

    public async Task<bool> RenewAsync(
        string key,
        string token,
        TimeSpan leaseTime,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var leaseMilliseconds = checked((long)Math.Ceiling(leaseTime.TotalMilliseconds));
        var result = await database.ScriptEvaluateAsync(
            RenewScript,
            [ToRedisKey(key)],
            [(RedisValue)token, leaseMilliseconds]);
        return (long)result == 1;
    }

    public string ToRedisKeyForTesting(string key) => ToRedisKey(key).ToString();

    private RedisKey ToRedisKey(string key) => keyPrefix + key;

    private static string NormalizeServiceName(string serviceName) =>
        serviceName.Trim().ToLowerInvariant();
}

public sealed class InMemoryRedisCommandLockStore : IRedisCommandLockStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<string, LockEntry> locks = new(StringComparer.Ordinal);

    public Task<bool> TryAcquireAsync(
        string key,
        string token,
        TimeSpan leaseTime,
        CancellationToken cancellationToken)
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
            if (locks.TryGetValue(key, out var current) &&
                string.Equals(current.Token, token, StringComparison.Ordinal))
            {
                locks.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> RenewAsync(
        string key,
        string token,
        TimeSpan leaseTime,
        CancellationToken cancellationToken)
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
