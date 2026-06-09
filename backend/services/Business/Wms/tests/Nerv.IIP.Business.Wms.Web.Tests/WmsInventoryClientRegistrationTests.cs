using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.ServiceAuth;
using Polly.CircuitBreaker;
using Polly.Timeout;

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

    [Fact]
    public async Task Registered_http_inventory_movement_client_times_out_slow_inventory_response()
    {
        var handler = new SlowInventoryHttpMessageHandler(TimeSpan.FromSeconds(2));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:BaseUrl"] = "http://inventory.local",
                ["Inventory:HttpClient:TimeoutSeconds"] = "0.2",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new PrimaryHandlerFilter(handler));
        services.AddWmsInventoryMovementClient(configuration, new TestWebHostEnvironment("Production"));
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IInventoryMovementClient>();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Record.ExceptionAsync(() =>
            client.PostMovementAsync(NewMovementRequest(quantity: 1m), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1)));
        stopwatch.Stop();

        Assert.NotNull(exception);
        Assert.IsType<TimeoutRejectedException>(exception);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Elapsed: {stopwatch.Elapsed}");
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Registered_http_inventory_movement_client_counts_timeouts_toward_circuit_breaker()
    {
        var handler = new SlowInventoryHttpMessageHandler(TimeSpan.FromSeconds(2));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:BaseUrl"] = "http://inventory.local",
                ["Inventory:HttpClient:TimeoutSeconds"] = "0.2",
                ["Inventory:HttpClient:CircuitBreaker:FailureRatio"] = "1",
                ["Inventory:HttpClient:CircuitBreaker:MinimumThroughput"] = "2",
                ["Inventory:HttpClient:CircuitBreaker:SamplingDurationSeconds"] = "5",
                ["Inventory:HttpClient:CircuitBreaker:BreakDurationSeconds"] = "1",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new PrimaryHandlerFilter(handler));
        services.AddWmsInventoryMovementClient(configuration, new TestWebHostEnvironment("Production"));
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IInventoryMovementClient>();

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            client.PostMovementAsync(NewMovementRequest(quantity: 1m), CancellationToken.None));
        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            client.PostMovementAsync(NewMovementRequest(quantity: 1m), CancellationToken.None));

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            client.PostMovementAsync(NewMovementRequest(quantity: 1m), CancellationToken.None));

        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("Inventory:HttpClient:CircuitBreaker:MinimumThroughput", "1", "greater than or equal to 2")]
    [InlineData("Inventory:HttpClient:CircuitBreaker:SamplingDurationSeconds", "0.1", "greater than or equal to 0.5 seconds")]
    [InlineData("Inventory:HttpClient:CircuitBreaker:BreakDurationSeconds", "0.1", "greater than or equal to 0.5 seconds")]
    public void Inventory_resilience_registration_rejects_invalid_circuit_breaker_configuration(
        string key,
        string value,
        string expectedMessage)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:BaseUrl"] = "http://inventory.local",
                [key] = value,
            })
            .Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddWmsInventoryMovementClient(configuration, new TestWebHostEnvironment("Production")));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registered_http_inventory_movement_client_does_not_retry_failed_inventory_post()
    {
        var handler = new UnavailableInventoryHttpMessageHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:BaseUrl"] = "http://inventory.local",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new PrimaryHandlerFilter(handler));
        services.AddWmsInventoryMovementClient(configuration, new TestWebHostEnvironment("Production"));
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IInventoryMovementClient>();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PostMovementAsync(NewMovementRequest(quantity: 1m), CancellationToken.None));

        Assert.Equal(1, handler.Calls);
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

    private sealed class SlowInventoryHttpMessageHandler(TimeSpan delay) : HttpMessageHandler
    {
        private int calls;

        public int Calls => calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"data":{"movementId":"mov-slow","onHandQuantity":1,"availableQuantity":1}}
                    """),
            };
        }
    }

    private sealed class UnavailableInventoryHttpMessageHandler : HttpMessageHandler
    {
        private int calls;

        public int Calls => calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class PrimaryHandlerFilter(HttpMessageHandler handler) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                builder.PrimaryHandler = handler;
            };
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
