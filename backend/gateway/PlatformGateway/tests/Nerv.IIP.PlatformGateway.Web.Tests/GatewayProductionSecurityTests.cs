using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayProductionSecurityTests
{
    [Fact]
    public void Production_gateway_requires_cors_allowed_origins()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKey", "production-test-signing-key-that-is-long-enough");
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Security:Cors:AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_gateway_allows_only_configured_console_origin()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKey", "production-test-signing-key-that-is-long-enough");
                builder.UseSetting("Security:Cors:AllowedOrigins:0", "https://console.example.test");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token-that-is-long-enough");
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
    }
}
