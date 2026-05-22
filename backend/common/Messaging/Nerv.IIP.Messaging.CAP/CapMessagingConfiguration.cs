using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Savorboard.CAP.InMemoryMessageQueue;

namespace Nerv.IIP.Messaging.CAP;

public static class CapMessagingConfiguration
{
    public const string ProviderConfigurationKey = "Messaging:Provider";
    public const string InMemoryProvider = "InMemory";
    public const string RabbitMqProvider = "RabbitMQ";

    public static CapOptions UseConfiguredTransport(this CapOptions options, IConfiguration configuration)
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

        throw new InvalidOperationException(
            $"Unsupported {ProviderConfigurationKey} '{provider}'. Supported values are '{InMemoryProvider}' and '{RabbitMqProvider}'.");
    }

    private static int ReadRabbitMqPort(IConfiguration configuration)
    {
        return int.TryParse(configuration["RabbitMQ:Port"], out var port) && port > 0 ? port : 5672;
    }
}
