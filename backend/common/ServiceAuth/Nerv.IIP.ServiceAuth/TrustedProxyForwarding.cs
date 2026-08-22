using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.ServiceAuth;

public static class TrustedProxyForwarding
{
    private const string KnownProxiesKey = "Security:ForwardedHeaders:KnownProxies";
    private const string KnownNetworksKey = "Security:ForwardedHeaders:KnownNetworks";

    public static IServiceCollection AddNervIipTrustedProxyForwarding(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return services;
        }

        var proxies = Values(configuration, KnownProxiesKey);
        var networks = Values(configuration, KnownNetworksKey);
        if (proxies.Length == 0 && networks.Length == 0)
        {
            throw new InvalidOperationException(
                $"{KnownProxiesKey} or {KnownNetworksKey} is required outside Development.");
        }

        var parsedProxies = proxies.Select(value =>
            IPAddress.TryParse(value, out var address)
                ? address
                : throw new InvalidOperationException($"{KnownProxiesKey} contains invalid IP address '{value}'.")).ToArray();
        var parsedNetworks = networks.Select(value =>
            System.Net.IPNetwork.TryParse(value, out var network)
                ? network
                : throw new InvalidOperationException($"{KnownNetworksKey} contains invalid CIDR '{value}'.")).ToArray();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in parsedProxies)
            {
                options.KnownProxies.Add(proxy);
            }
            foreach (var network in parsedNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }

    private static string[] Values(IConfiguration configuration, string key)
    {
        var indexed = configuration.GetSection(key)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        if (indexed.Length > 0)
        {
            return indexed;
        }

        return (configuration[key] ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
