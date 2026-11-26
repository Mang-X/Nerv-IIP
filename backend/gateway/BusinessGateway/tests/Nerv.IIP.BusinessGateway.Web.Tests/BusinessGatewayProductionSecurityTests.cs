using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayProductionSecurityTests
{
    [Fact]
    public void Production_business_gateway_requires_cors_allowed_origins()
    {
        using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            BusinessGatewayTestHostProfile.ServiceBaseUrls,
            builder => builder.UseEnvironment("Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Security:Cors:AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_business_gateway_allows_only_configured_business_console_origin()
    {
        await using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            BusinessGatewayTestHostProfile.ServiceBaseUrls,
            builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Security:Cors:AllowedOrigins:0", "https://business.example.test");
                builder.UseSetting("Security:ForwardedHeaders:TrustedProxies", "192.0.2.10");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
            });
        using var client = BusinessGatewayTestHost.CreateGatedClient(
            factory,
            baseAddress: new Uri("https://localhost"));

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/business-console/v1/master-data/skus");
        request.Headers.TryAddWithoutValidation("Origin", "https://business.example.test");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal("https://business.example.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public void Production_business_gateway_requires_demand_planning_base_url()
    {
        using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            configureBuilder: builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Security:Cors:AllowedOrigins:0", "https://business.example.test");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
                builder.UseSetting("Iam:BaseUrl", "http://iam.local");
                builder.UseSetting("MasterData:BaseUrl", "http://master-data.local");
                builder.UseSetting("Inventory:BaseUrl", "http://inventory.local");
                builder.UseSetting("Quality:BaseUrl", "http://quality.local");
                builder.UseSetting("ProductEngineering:BaseUrl", "http://engineering.local");
                builder.UseSetting("Mes:BaseUrl", "http://mes.local");
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("DemandPlanning:BaseUrl", exception.Message, StringComparison.Ordinal);
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
        BusinessGatewayTestHost.CreateDedicatedFactory(
            BusinessGatewayTestHostProfile.ServiceBaseUrls, builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Security:Cors:AllowedOrigins", "https://console.example.test");
            builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
            builder.UseSetting("Security:ForwardedHeaders:TrustedProxies", trustedProxies);
        });
}
