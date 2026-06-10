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
    private const string DevelopmentEnvironmentName = "Development";

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
            options.UseRabbitMQ(rabbitMqOptions =>
            {
                rabbitMqOptions.HostName = configuration["RabbitMQ:HostName"] ?? "localhost";
                rabbitMqOptions.Port = ReadRabbitMqPort(configuration);
                rabbitMqOptions.UserName = configuration["RabbitMQ:UserName"] ?? "guest";
                rabbitMqOptions.Password = configuration["RabbitMQ:Password"] ?? "guest";
            });
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

    private static int ReadRabbitMqPort(IConfiguration configuration)
    {
        return int.TryParse(configuration["RabbitMQ:Port"], out var port) && port > 0 ? port : 5672;
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
