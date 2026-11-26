using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.ServiceAuth;

public static class GatewayForwardedHeaders
{
    public static void AddNervIipGatewayForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        const string configurationKey = "Security:ForwardedHeaders:TrustedProxies";
        var entries = configuration[configurationKey]?.Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

        if (entries.Length == 0)
        {
            if (environment.IsDevelopment())
            {
                return;
            }

            throw new InvalidOperationException($"{configurationKey} is required outside Development.");
        }

        var proxies = new List<IPAddress>();
        var networks = new List<System.Net.IPNetwork>();
        foreach (var entry in entries)
        {
            if (IPAddress.TryParse(entry, out var address)
                && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any))
            {
                proxies.Add(address);
            }
            else if (System.Net.IPNetwork.TryParse(entry, out var network) && network.PrefixLength > 0)
            {
                networks.Add(network);
            }
            else
            {
                throw new InvalidOperationException($"{configurationKey} requires proxy IP addresses or restricted CIDR networks.");
            }
        }

        services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });
    }
}
