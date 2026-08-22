using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nerv.IIP.ServiceAuth.Tests;

public sealed class TrustedProxyForwardingTests
{
    [Fact]
    public void Production_requires_an_explicit_trusted_proxy_or_network()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNervIipTrustedProxyForwarding(Configuration(), Environment("Production")));

        Assert.Contains("Security:ForwardedHeaders:KnownProxies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_configures_one_hop_proto_and_for_from_allowlisted_sources()
    {
        var services = new ServiceCollection();
        services.AddNervIipTrustedProxyForwarding(
            Configuration(
                ("Security:ForwardedHeaders:KnownProxies:0", "127.0.0.1"),
                ("Security:ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12")),
            Environment("Production"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(IPAddress.Loopback, Assert.Single(options.KnownProxies));
        Assert.Equal(System.Net.IPNetwork.Parse("172.16.0.0/12"), Assert.Single(options.KnownIPNetworks));
    }

    [Theory]
    [InlineData("Security:ForwardedHeaders:KnownProxies", "not-an-ip")]
    [InlineData("Security:ForwardedHeaders:KnownNetworks", "not-a-cidr")]
    public void Malformed_trust_boundary_fails_at_startup(string key, string value)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNervIipTrustedProxyForwarding(Configuration((key, value)), Environment("Production")));
    }

    [Fact]
    public void Development_does_not_require_a_proxy_contract()
    {
        var services = new ServiceCollection();
        services.AddNervIipTrustedProxyForwarding(Configuration(), Environment("Development"));
    }

    [Theory]
    [InlineData("127.0.0.1", "https")]
    [InlineData("10.0.0.10", "http")]
    public async Task Forwarded_scheme_is_applied_only_for_the_allowlisted_proxy(
        string remoteAddress,
        string expectedScheme)
    {
        var services = new ServiceCollection();
        services.AddNervIipTrustedProxyForwarding(
            Configuration(("Security:ForwardedHeaders:KnownProxies", "127.0.0.1")),
            Environment("Production"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.Invoke(context);

        Assert.Equal(expectedScheme, context.Request.Scheme);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();

    private static IHostEnvironment Environment(string name) => new StubHostEnvironment { EnvironmentName = name };

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
