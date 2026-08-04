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
}
