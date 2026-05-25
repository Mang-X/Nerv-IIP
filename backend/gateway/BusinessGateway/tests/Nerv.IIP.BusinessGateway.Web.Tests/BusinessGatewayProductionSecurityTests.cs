using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayProductionSecurityTests
{
    [Fact]
    public void Production_business_gateway_requires_cors_allowed_origins()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
                builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
                builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Security:Cors:AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_business_gateway_allows_only_configured_business_console_origin()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
                builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
                builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
                builder.UseSetting("Security:Cors:AllowedOrigins:0", "https://business.example.test");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/business-console/v1/master-data/skus");
        request.Headers.TryAddWithoutValidation("Origin", "https://business.example.test");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal("https://business.example.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
