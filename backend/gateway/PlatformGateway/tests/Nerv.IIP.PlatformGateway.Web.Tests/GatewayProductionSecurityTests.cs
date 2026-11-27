using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayProductionSecurityTests
{
    [Fact]
    public void Production_gateway_requires_cors_allowed_origins()
    {
        using var factory = PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:JwksJson", GatewayTestTokens.PublicJwksJson());
                ConfigureServiceBaseUrls(builder);
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Security:Cors:AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_gateway_allows_only_configured_console_origin()
    {
        await using var factory = PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:JwksJson", GatewayTestTokens.PublicJwksJson());
                builder.UseSetting("Security:Cors:AllowedOrigins:0", "https://console.example.test");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
                ConfigureServiceBaseUrls(builder);
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/console/v1/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", "https://console.example.test");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal("https://console.example.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var actualRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        actualRequest.Headers.TryAddWithoutValidation("Origin", "https://console.example.test");
        var actualResponse = await client.SendAsync(actualRequest);
        actualResponse.EnsureSuccessStatusCode();
        var exposedHeaders = actualResponse.Headers.GetValues("Access-Control-Expose-Headers").Single();
        Assert.Contains(GatewayAuthResponseHeaders.LoginFailure, exposedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GatewayAuthResponseHeaders.LockoutUntilUtc, exposedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GatewayAuthResponseHeaders.RemainingAttempts, exposedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_gateway_requires_apphub_base_url()
    {
        using var factory = PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:JwksJson", GatewayTestTokens.PublicJwksJson());
                builder.UseSetting("Security:Cors:AllowedOrigins:0", "https://console.example.test");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
                builder.UseSetting("Iam:BaseUrl", "http://iam.local");
                builder.UseSetting("Ops:BaseUrl", "http://ops.local");
                builder.UseSetting("Notification:BaseUrl", "http://notification.local");
        builder.UseSetting("Security:ForwardedHeaders:TrustedProxies", "192.0.2.10");
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AppHub:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    private static void ConfigureServiceBaseUrls(IWebHostBuilder builder)
    {
        builder.UseSetting("AppHub:BaseUrl", "http://apphub.local");
        builder.UseSetting("FileStorage:BaseUrl", "http://filestorage.local");
        builder.UseSetting("Iam:BaseUrl", "http://iam.local");
        builder.UseSetting("Ops:BaseUrl", "http://ops.local");
        builder.UseSetting("Notification:BaseUrl", "http://notification.local");
        builder.UseSetting("Security:ForwardedHeaders:TrustedProxies", "192.0.2.10");
    }

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10", 404)]
    [InlineData("192.0.2.10", "192.0.2.0/24", 404)]
    [InlineData("198.51.100.10", "192.0.2.10", 307)]
    [InlineData("127.0.0.1", "192.0.2.10", 307)]
    public async Task Forwarded_https_scheme_is_accepted_only_from_a_trusted_proxy(
        string remoteIp, string trustedProxies, int expectedStatusCode)
    {
        await using var factory = CreateProxyFactory(trustedProxies);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Scheme = "http";
            context.Request.Host = new Microsoft.AspNetCore.Http.HostString("gateway.example.test");
            context.Request.Path = "/probe-unmapped-path";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        Assert.Equal(expectedStatusCode, response.Response.StatusCode);
        if (expectedStatusCode == 404)
        {
            Assert.True(response.Response.Headers.ContainsKey("Strict-Transport-Security"));
        }
        else
        {
            Assert.Equal("https://gateway.example.test/probe-unmapped-path", response.Response.Headers.Location);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.0.0.0/0")]
    public async Task Production_gateway_requires_restricted_trusted_proxy_configuration(string trustedProxies)
    {
        await using var factory = CreateProxyFactory(trustedProxies);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Security:ForwardedHeaders:TrustedProxies", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateProxyFactory(string trustedProxies) =>
        PlatformGatewayTestHost.CreateFactory().WithWebHostBuilder(builder =>
        {
            ConfigureServiceBaseUrls(builder);
            builder.UseSetting("Iam:Jwt:JwksJson", GatewayTestTokens.PublicJwksJson());
            builder.UseEnvironment("Production");
            builder.UseSetting("Security:Cors:AllowedOrigins", "https://console.example.test");
            builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
            builder.UseSetting("Security:ForwardedHeaders:TrustedProxies", trustedProxies);
        });
}
