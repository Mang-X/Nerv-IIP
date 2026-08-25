using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nerv.IIP.Sdk.FileStorage;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class FileStorageHttpClientConfigurationTests
{
    [Fact]
    public async Task Typed_client_applies_explicit_connection_and_request_budgets()
    {
        var capture = new FileStoragePrimaryHandlerCaptureFilter();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting("FileStorage:ConnectTimeout", "00:00:00.250");
                builder.UseSetting("FileStorage:RequestTimeout", "00:00:00.500");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture));
            });

        var client = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IFileStorageClient));

        Assert.Equal(TimeSpan.FromMilliseconds(500), client.Timeout);
        var handler = Assert.IsType<SocketsHttpHandler>(capture.PrimaryHandler);
        Assert.Equal(TimeSpan.FromMilliseconds(250), handler.ConnectTimeout);
    }

    [Theory]
    [InlineData("FileStorage:ConnectTimeout")]
    [InlineData("FileStorage:RequestTimeout")]
    public void Client_budgets_must_be_positive(string setting)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting(setting, "00:00:00");
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(setting, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("positive", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class FileStoragePrimaryHandlerCaptureFilter : IHttpMessageHandlerBuilderFilter
{
    public HttpMessageHandler? PrimaryHandler { get; private set; }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name == nameof(IFileStorageClient))
        {
            PrimaryHandler = builder.PrimaryHandler;
        }
    };
}
