using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.PlatformGateway.Web;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayHttpClientResilienceTests
{
    [Fact]
    public async Task Non_idempotent_gateway_clients_do_not_retry_server_errors()
    {
        var handler = new AlwaysUnavailableHandler();
        var services = new ServiceCollection();
        services
            .AddHttpClient("non-idempotent-safe")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddGatewayNonIdempotentSafeResilience();
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("non-idempotent-safe");

        var response = await client.GetAsync("http://downstream.local/unavailable");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class AlwaysUnavailableHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
