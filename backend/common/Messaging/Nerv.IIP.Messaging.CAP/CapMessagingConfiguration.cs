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
    public const string RabbitMqConnectionStringConfigurationKey = "Messaging:RabbitMQ:ConnectionString";
    public const string RabbitMqConnectionStringFallbackKey = "ConnectionStrings:rabbitmq";
    public const string FailedRetryIntervalConfigurationKey = "Cap:FailedRetryInterval";
    public const string FallbackWindowLookbackSecondsConfigurationKey = "Cap:FallbackWindowLookbackSeconds";
    private const string DevelopmentEnvironmentName = "Development";
    private const int MinimumFallbackWindowLookbackSeconds = 30;

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
            var redisConnectionString = ReadRedisConnectionString(configuration);
            var redisConfiguration = ConfigurationOptions.Parse(redisConnectionString);
            redisConfiguration.AbortOnConnectFail = false;
            options.UseRedis(redisOptions => redisOptions.Configuration = redisConfiguration);
            return options;
        }

        throw new InvalidOperationException(
            $"Unsupported {ProviderConfigurationKey} '{provider}'. Supported values are '{InMemoryProvider}', '{RabbitMqProvider}' and '{RedisProvider}'.");
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
