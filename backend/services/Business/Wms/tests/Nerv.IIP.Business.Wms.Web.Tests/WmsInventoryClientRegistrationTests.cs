using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsInventoryClientRegistrationTests
{
    [Fact]
    public void Default_inventory_movement_client_registration_uses_http_client_not_noop()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:BaseUrl"] = "http://inventory.local",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddWmsInventoryMovementClient(configuration, new TestWebHostEnvironment("Production"));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IInventoryMovementClient>();

        Assert.IsType<HttpInventoryMovementClient>(client);
        Assert.IsNotType<NoopInventoryMovementClient>(client);
    }

    [Fact]
    public void Production_inventory_movement_client_registration_requires_inventory_base_url()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddWmsInventoryMovementClient(configuration, new TestWebHostEnvironment("Production")));

        Assert.Contains("Inventory:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_inventory_movement_client_posts_to_inventory_movements_api_and_reads_wrapped_response()
    {
        var handler = new RecordingHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://inventory.local"),
        };
        var client = new HttpInventoryMovementClient(httpClient, new TestInternalServiceTokenProvider());

        var result = await client.PostMovementAsync(NewMovementRequest(quantity: -2.5m), CancellationToken.None);

        Assert.Equal("mov-001", result.InventoryMovementId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/inventory/v1/movements", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("test-internal-token", handler.Request.Headers.Authorization.Parameter);
        using var document = JsonDocument.Parse(handler.Body!);
        Assert.Equal("count-adjustment", document.RootElement.GetProperty("movementType").GetString());
        Assert.Equal(-2.5m, document.RootElement.GetProperty("quantity").GetDecimal());
    }

    private static PostInventoryMovementRequest NewMovementRequest(decimal quantity)
    {
        return new PostInventoryMovementRequest(
            "org-001",
            "env-dev",
            "count-adjustment",
            "wms",
            "COUNT-001",
            null,
            "idem-count-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            null,
            null,
            "qualified",
            "company",
            null,
            quantity);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"data":{"movementId":"mov-001","onHandQuantity":7.5,"availableQuantity":7.5}}
                    """),
            };
        }
    }

    private sealed class TestInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Nerv.IIP.Business.Wms.Web.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
