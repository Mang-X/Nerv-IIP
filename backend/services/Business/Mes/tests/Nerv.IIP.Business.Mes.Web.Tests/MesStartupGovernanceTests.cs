using System.Net;
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
    public void MasterData_client_has_bounded_connection_and_total_request_timeouts()
    {
        var capture = new PrimaryHandlerCaptureFilter();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture);
        services.AddMesMasterDataHttpClient(new Uri("http://master-data"));
        using var provider = services.BuildServiceProvider();
        var masterDataClient = provider.GetRequiredService<MesMasterDataHttpClient>();

        Assert.Equal(TimeSpan.FromSeconds(10), masterDataClient.HttpClient.Timeout);
        var socketsHandler = Assert.IsType<SocketsHttpHandler>(capture.PrimaryHandler);
        Assert.Equal(TimeSpan.FromSeconds(5), socketsHandler.ConnectTimeout);
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
            if (builder.Name?.Contains(nameof(MesMasterDataHttpClient), StringComparison.Ordinal) == true)
            {
                PrimaryHandler = builder.PrimaryHandler;
            }
        };
    }
}
