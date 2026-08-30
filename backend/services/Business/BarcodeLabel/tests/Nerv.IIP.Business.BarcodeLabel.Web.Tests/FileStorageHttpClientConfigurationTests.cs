using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;
using Nerv.IIP.Sdk.FileStorage;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class FileStorageHttpClientConfigurationTests
{
    [Fact]
    public async Task Managed_clients_apply_authentication_redirect_policy_and_three_explicit_budgets()
    {
        var capture = new FileStoragePrimaryHandlerCaptureFilter();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting("FileStorage:ConnectTimeout", "00:00:00.250");
                builder.UseSetting("FileStorage:RequestTimeout", "00:00:00.500");
                builder.UseSetting("FileStorage:DownloadTimeout", "00:00:00.750");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture));
            });

        var metadataClient = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IFileStorageClient));
        var downloadClient = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(FileStorageClientOptions.DownloadClientName);

        Assert.Equal(new Uri("http://localhost:5104"), metadataClient.BaseAddress);
        Assert.Equal(TimeSpan.FromMilliseconds(500), metadataClient.Timeout);
        Assert.Equal("Bearer", metadataClient.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("test-internal-token", metadataClient.DefaultRequestHeaders.Authorization?.Parameter);
        var metadataHandler = Assert.IsType<SocketsHttpHandler>(capture.GetHandler(nameof(IFileStorageClient)));
        Assert.Equal(TimeSpan.FromMilliseconds(250), metadataHandler.ConnectTimeout);

        Assert.Equal(new Uri("http://localhost:5104"), downloadClient.BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, downloadClient.Timeout);
        Assert.Equal("Bearer", downloadClient.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("test-internal-token", downloadClient.DefaultRequestHeaders.Authorization?.Parameter);
        var downloadHandler = Assert.IsType<SocketsHttpHandler>(capture.GetHandler(FileStorageClientOptions.DownloadClientName));
        Assert.Equal(TimeSpan.FromMilliseconds(250), downloadHandler.ConnectTimeout);
        Assert.False(downloadHandler.AllowAutoRedirect);

        using var scope = factory.Services.CreateScope();
        Assert.IsType<HttpFileStorageLabelTemplateAssetAdapter>(
            scope.ServiceProvider.GetRequiredService<ILabelTemplateAssetPort>());
        Assert.Equal(
            TimeSpan.FromMilliseconds(750),
            scope.ServiceProvider.GetRequiredService<IOptions<FileStorageClientOptions>>().Value.DownloadTimeout);
    }

    [Theory]
    [InlineData("FileStorage:ConnectTimeout")]
    [InlineData("FileStorage:RequestTimeout")]
    [InlineData("FileStorage:DownloadTimeout")]
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
    private readonly Dictionary<string, HttpMessageHandler> primaryHandlers = new(StringComparer.Ordinal);

    public HttpMessageHandler? GetHandler(string clientName) =>
        primaryHandlers.GetValueOrDefault(clientName);

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name is nameof(IFileStorageClient) or FileStorageClientOptions.DownloadClientName)
        {
            primaryHandlers[builder.Name] = builder.PrimaryHandler;
        }
    };
}
