using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesStartupGovernanceTests
{
    [Fact]
    public async Task Inventory_typed_client_applies_explicit_connection_and_request_budgets()
    {
        var capture = new PrimaryHandlerCaptureFilter(nameof(MesInventoryHttpClient));
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

    /// <summary>
    /// #2780 首件门禁的生产接线：报工命令处理器必须能从**应用自己的容器**解析出来，且拿到的
    /// 首件门禁是会去问 Quality 的那一个。门禁参数已是必填构造参数，因此漏掉
    /// <c>Program.cs</c> 的注册时这里解析失败——不会退化成任何「读不到就放行」的实现。
    /// 用例不覆盖注册，正是为了让它承担这条接线。
    /// </summary>
    [Fact]
    public void Production_report_handler_resolves_the_quality_backed_first_article_gate()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<HttpMesFirstArticleGate>(
            scope.ServiceProvider.GetRequiredService<IMesFirstArticleGate>());
        Assert.NotNull(scope.ServiceProvider
            .GetRequiredService<IRequestHandler<RecordProductionReportCommand, ProductionReportCommandResult>>());
    }

    // #2780 起 Quality 客户端落在报工写事务内且每次报工都要走一趟：预算缺失会让一次挂起的
    // Quality 请求把 UoW 事务按默认 100 秒挂住，因此与 Inventory 同等对待。
    [Fact]
    public async Task Quality_typed_client_applies_explicit_connection_and_request_budgets()
    {
        var capture = new PrimaryHandlerCaptureFilter(nameof(MesQualityHttpClient));
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Mes:QualityClient:ConnectTimeout", "00:00:00.125");
                builder.UseSetting("Mes:QualityClient:RequestTimeout", "00:00:00.750");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture));
            });

        var client = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(MesQualityHttpClient));

        Assert.Equal(TimeSpan.FromMilliseconds(750), client.Timeout);
        var handler = Assert.IsType<SocketsHttpHandler>(capture.PrimaryHandler);
        Assert.Equal(TimeSpan.FromMilliseconds(125), handler.ConnectTimeout);
    }

    [Theory]
    [InlineData("Mes:InventoryClient:ConnectTimeout")]
    [InlineData("Mes:InventoryClient:RequestTimeout")]
    [InlineData("Mes:QualityClient:ConnectTimeout")]
    [InlineData("Mes:QualityClient:RequestTimeout")]
    public void Typed_client_budgets_must_be_positive(string setting)
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

    private sealed class PrimaryHandlerCaptureFilter(string clientName) : IHttpMessageHandlerBuilderFilter
    {
        public HttpMessageHandler? PrimaryHandler { get; private set; }

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name == clientName)
            {
                PrimaryHandler = builder.PrimaryHandler;
            }
        };
    }
}
