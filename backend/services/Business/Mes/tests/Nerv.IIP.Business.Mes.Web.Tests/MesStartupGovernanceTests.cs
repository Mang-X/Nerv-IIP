using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesStartupGovernanceTests
{
    [Fact]
    public async Task Inventory_typed_client_applies_explicit_connection_and_request_budgets()
    {
        var capture = new PrimaryHandlerCaptureFilter();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Mes:InventoryClient:ConnectTimeout", "00:00:00.250");
                builder.UseSetting("Mes:InventoryClient:RequestTimeout", "00:00:00.500");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture));
            });

        var client = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(MesInventoryHttpClient));

        Assert.Equal(TimeSpan.FromMilliseconds(500), client.Timeout);
        var handler = Assert.IsType<SocketsHttpHandler>(capture.PrimaryHandler);
        Assert.Equal(TimeSpan.FromMilliseconds(250), handler.ConnectTimeout);
    }

    [Theory]
    [InlineData("Mes:InventoryClient:ConnectTimeout")]
    [InlineData("Mes:InventoryClient:RequestTimeout")]
    public void Inventory_client_budgets_must_be_positive(string setting)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting(setting, "00:00:00"));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(setting, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("positive", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // MES has no code-analysis endpoint yet; this test only covers startup migration governance.
    [Fact]
    public async Task AutoMigrate_true_outside_development_is_rejected()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_mes_governance;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                        ["Persistence:AutoMigrate"] = "true",
                    }));
            });

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/swagger/v1/swagger.json");
        });

        Assert.Contains(exception.Flatten(), x =>
            x is InvalidOperationException
            && x.Message.Contains("Persistence:AutoMigrate=true", StringComparison.Ordinal));
    }

    private sealed class PrimaryHandlerCaptureFilter : IHttpMessageHandlerBuilderFilter
    {
        public HttpMessageHandler? PrimaryHandler { get; private set; }

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name == nameof(MesInventoryHttpClient))
            {
                PrimaryHandler = builder.PrimaryHandler;
            }
        };
    }
}
