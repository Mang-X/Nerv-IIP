using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Savorboard.CAP.InMemoryMessageQueue;
using StackExchange.Redis;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// Configures the CAP transport for services that ship with both in-memory and RabbitMQ binaries.
/// The selected provider removes RabbitMQ as a runtime requirement for single-node deployments.
/// </summary>
public static class CapMessagingConfiguration
{
    public const string ProviderConfigurationKey = "Messaging:Provider";
    public const string InMemoryProvider = "InMemory";
    public const string RabbitMqProvider = "RabbitMQ";
    public const string RedisProvider = "Redis";
    public const string RedisConnectionStringConfigurationKey = "Messaging:Redis:ConnectionString";
    public const string RedisConnectionStringFallbackKey = "ConnectionStrings:Redis";
    public const string RedisCachingFallbackKey = "Caching:Redis";
    public const string RedisConnectionPoolSizeConfigurationKey = "Messaging:Redis:ConnectionPoolSize";
    public const string RabbitMqConnectionStringConfigurationKey = "Messaging:RabbitMQ:ConnectionString";
    public const string RabbitMqConnectionStringFallbackKey = "ConnectionStrings:rabbitmq";
    public const string FailedRetryIntervalConfigurationKey = "Cap:FailedRetryInterval";
    public const string FallbackWindowLookbackSecondsConfigurationKey = "Cap:FallbackWindowLookbackSeconds";
    public const string VersionConfigurationKey = "Cap:Version";
    public const string TopicNamePrefixConfigurationKey = "Cap:TopicNamePrefix";
    private const string DevelopmentEnvironmentName = "Development";
    private const int MinimumFallbackWindowLookbackSeconds = 30;
    private const int MinimumRedisConnectionPoolSize = 1;

    /// <summary>
    /// CAP 的 Redis 传输默认开 10 条 <c>ConnectionMultiplexer</c>
    /// （上游 <c>CapRedisOptionsPostConfigure</c> 在取值为 <c>default</c> 时回填 10）。本仓默认收到 1，理由如下。
    ///
    /// <para><b>为什么默认 1。</b>上游 <c>AsyncLazyRedisConnection</c> 派生自 <c>Lazy&lt;Task&lt;RedisConnection&gt;&gt;</c>，
    /// 它的 <c>CreatedConnection</c> 是 <c>IsValueCreated ? Value.GetAwaiter().GetResult() : null</c>。
    /// 在 <c>Lazy&lt;Task&lt;…&gt;&gt;</c> 上 <c>IsValueCreated</c> 只表示工厂已跑、Task 已产生，<b>不表示 Task 已完成</b>，
    /// 于是连接在途时读它就是<b>同步阻塞当前线程</b>。<c>RedisConnectionPool.ConnectAsync()</c> 里第一个调用者走
    /// <c>await lazy</c> 不阻塞，<b>第二个及以后</b>遍历到「已创建但在途」的 lazy 时解引用 <c>CreatedConnection</c>
    /// ⇒ 全部同步阻塞池线程。宿主一次性起十几个消费组时，这批阻塞叠在 <c>ThreadPool</c> 的
    /// 每秒约一条的注入速率上，表现为 <c>EXISTS</c>/<c>XGROUP</c> 跨过 SE.Redis 的 5s <c>SyncTimeout</c>。
    /// 取值为 1 时，第一条连接建成后 <c>_poolAlreadyConfigured</c> 恒 true、<c>QuietConnection</c> 直接返回那条已完成的连接，
    /// 上面那个 <c>foreach</c> 再也到不了阻塞点。</para>
    ///
    /// <para><b>取舍。</b>SE.Redis 的 <c>ConnectionMultiplexer</c> 本身是多路复用设计、官方推荐单例，
    /// 因此单条连接在正常负载下够用；但<b>吞吐上界确实从 10 条连接降到 1 条</b>——
    /// 单条多路复用连接上的大 payload 或慢命令会排在同一条 TCP 管道上互相挡道。</para>
    ///
    /// <para><b>什么情况下该调大。</b>当某个宿主被观测到「Redis 侧 CPU 与网络都不饱和、
    /// 但 SE.Redis 超时诊断里 <c>qs</c>（排队中的同步命令）持续偏高」时，说明瓶颈在单条管道而非线程池，
    /// 可通过 <see cref="RedisConnectionPoolSizeConfigurationKey"/> 按宿主调大。</para>
    ///
    /// <para><b>调大会重新打开哪个窗口。</b>取值 &gt; 1 会让连接池重新存在「多条 slot、部分在途」的状态，
    /// 也就重新打开上面描述的 <c>CreatedConnection</c> 同步阻塞窗口；该窗口只在进程启动后的首轮建连期间存在，
    /// 但首轮恰好也是十几个消费组同时订阅的时刻。调大前应确认该宿主的消费组数量与 <c>ThreadPool</c> 最小线程数。</para>
    /// </summary>
    private const int DefaultRedisConnectionPoolSize = 1;

    public static CapOptions UseConfiguredRecovery(
        this CapOptions options,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var retryInterval = ReadOptionalPositiveInt(configuration, FailedRetryIntervalConfigurationKey, minimum: 1);
        if (retryInterval.HasValue)
        {
            options.FailedRetryInterval = retryInterval.Value;
        }

        var fallbackLookback = ReadOptionalPositiveInt(
            configuration,
            FallbackWindowLookbackSecondsConfigurationKey,
            MinimumFallbackWindowLookbackSeconds);
        if (fallbackLookback.HasValue)
        {
            options.FallbackWindowLookbackSeconds = fallbackLookback.Value;
        }

        return options;
    }

    public static CapOptions UseConfiguredTransport(
        this CapOptions options,
        IConfiguration configuration,
        string? environmentName = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration[ProviderConfigurationKey];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = InMemoryProvider;
        }

        if (string.Equals(provider, InMemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            EnsureInMemoryTransportAllowed(configuration, environmentName);
            options.UseInMemoryMessageQueue();
            return options;
        }

        if (string.Equals(provider, RabbitMqProvider, StringComparison.OrdinalIgnoreCase))
        {
            options.UseRabbitMQ(rabbitMqOptions => ApplyRabbitMqConnection(rabbitMqOptions, configuration));
            return options;
        }

        if (string.Equals(provider, RedisProvider, StringComparison.OrdinalIgnoreCase))
        {
            ApplyRedisSessionIsolation(options, configuration);
            var redisConnectionString = ReadRedisConnectionString(configuration);
            var redisConfiguration = ConfigurationOptions.Parse(redisConnectionString);
            redisConfiguration.AbortOnConnectFail = false;
            var redisConnectionPoolSize = ReadOptionalPositiveInt(
                configuration,
                RedisConnectionPoolSizeConfigurationKey,
                MinimumRedisConnectionPoolSize) ?? DefaultRedisConnectionPoolSize;
            options.UseRedis(redisOptions =>
            {
                redisOptions.Configuration = redisConfiguration;

                // 下界必须挡在这里：上游 CapRedisOptionsPostConfigure 对取值为 default（0）的
                // ConnectionPoolSize 会静默回填 10，配错成 0 时不会报错，只会悄悄退回上游默认。
                redisOptions.ConnectionPoolSize = (uint)redisConnectionPoolSize;
            });
            return options;
        }

        throw new InvalidOperationException(
            $"Unsupported {ProviderConfigurationKey} '{provider}'. Supported values are '{InMemoryProvider}', '{RabbitMqProvider}' and '{RedisProvider}'.");
    }

    internal static void ApplyRedisSessionIsolation(CapOptions options, IConfiguration configuration)
    {
        var version = configuration[VersionConfigurationKey];
        if (!string.IsNullOrWhiteSpace(version))
        {
            options.Version = version;
        }

        var topicNamePrefix = configuration[TopicNamePrefixConfigurationKey];
        if (!string.IsNullOrWhiteSpace(topicNamePrefix))
        {
            options.TopicNamePrefix = topicNamePrefix;
        }
    }

    /// <summary>
    /// Configures RabbitMQ host/port/credentials. The orchestrator (Aspire) injects the broker
    /// endpoint as an AMQP connection string (<see cref="RabbitMqConnectionStringFallbackKey"/>),
    /// so that is parsed first; explicit <c>RabbitMQ:*</c> keys override individual fields, and
    /// localhost/guest defaults apply only when nothing else is provided. Without this the broker
    /// endpoint is unknown and CAP falls back to localhost:5672, which is unreachable for
    /// container-hosted brokers and silently breaks all cross-service consumption.
    /// </summary>
    internal static void ApplyRabbitMqConnection(RabbitMQOptions rabbitMqOptions, IConfiguration configuration)
    {
        var connectionString = configuration[RabbitMqConnectionStringConfigurationKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration[RabbitMqConnectionStringFallbackKey];
        }

        string? hostFromConnection = null;
        int? portFromConnection = null;
        string? userFromConnection = null;
        string? passwordFromConnection = null;
        string? virtualHostFromConnection = null;

        if (!string.IsNullOrWhiteSpace(connectionString)
            && Uri.TryCreate(connectionString, UriKind.Absolute, out var amqpUri))
        {
            hostFromConnection = amqpUri.Host;
            if (amqpUri.Port > 0)
            {
                portFromConnection = amqpUri.Port;
            }

            var userInfoParts = amqpUri.UserInfo.Split(':', 2);
            if (userInfoParts.Length > 0 && !string.IsNullOrEmpty(userInfoParts[0]))
            {
                userFromConnection = Uri.UnescapeDataString(userInfoParts[0]);
            }

            if (userInfoParts.Length > 1)
            {
                passwordFromConnection = Uri.UnescapeDataString(userInfoParts[1]);
            }

            var path = amqpUri.AbsolutePath.TrimStart('/');
            if (!string.IsNullOrEmpty(path))
            {
                virtualHostFromConnection = Uri.UnescapeDataString(path);
            }
        }

        rabbitMqOptions.HostName = configuration["RabbitMQ:HostName"] ?? hostFromConnection ?? "localhost";
        rabbitMqOptions.Port = int.TryParse(configuration["RabbitMQ:Port"], out var explicitPort) && explicitPort > 0
            ? explicitPort
            : portFromConnection ?? 5672;
        rabbitMqOptions.UserName = configuration["RabbitMQ:UserName"] ?? userFromConnection ?? "guest";
        rabbitMqOptions.Password = configuration["RabbitMQ:Password"] ?? passwordFromConnection ?? "guest";

        var explicitVirtualHost = configuration["RabbitMQ:VirtualHost"];
        if (!string.IsNullOrWhiteSpace(explicitVirtualHost))
        {
            rabbitMqOptions.VirtualHost = explicitVirtualHost;
        }
        else if (!string.IsNullOrEmpty(virtualHostFromConnection))
        {
            rabbitMqOptions.VirtualHost = virtualHostFromConnection;
        }
    }

    private static string ReadRedisConnectionString(IConfiguration configuration)
    {
        var redisConnectionString = configuration[RedisConnectionStringConfigurationKey];
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            redisConnectionString = configuration[RedisConnectionStringFallbackKey];
        }

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            redisConnectionString = configuration[RedisCachingFallbackKey];
        }

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return redisConnectionString;
        }

        throw new InvalidOperationException(
            "Redis CAP transport requires a Redis connection string. " +
            $"Set {RedisConnectionStringConfigurationKey}; fallback keys are {RedisConnectionStringFallbackKey} and {RedisCachingFallbackKey}.");
    }

    private static int? ReadOptionalPositiveInt(
        IConfiguration configuration,
        string key,
        int minimum)
    {
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (!int.TryParse(configured, out var value) || value < minimum)
        {
            throw new InvalidOperationException($"{key} must be an integer greater than or equal to {minimum}.");
        }

        return value;
    }

    private static void EnsureInMemoryTransportAllowed(IConfiguration configuration, string? environmentName)
    {
        environmentName ??= configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"];
        if (string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            "CAP InMemory transport is only allowed in Development because queued integration events are lost on process restart. " +
            $"Set {ProviderConfigurationKey}={RabbitMqProvider} or {ProviderConfigurationKey}={RedisProvider} for non-Development environments.");
    }
}
