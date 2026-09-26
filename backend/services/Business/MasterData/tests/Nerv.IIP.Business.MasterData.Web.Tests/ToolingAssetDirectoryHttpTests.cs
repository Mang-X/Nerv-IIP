using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ToolingAssetDirectoryHttpTests
{
    [Fact]
    public async Task Get_wires_keyword_status_skip_and_take_and_returns_string_status()
    {
        await using var factory = new ToolingAssetDirectoryHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ToolingAssets.AddRange(
                CreateTooling("org-001", "env-dev", "TOOL-010", "precision-jig", ToolingAssetStatus.Maintenance),
                CreateTooling("org-001", "env-dev", "TOOL-020", "precision-jig", ToolingAssetStatus.Maintenance),
                CreateTooling("org-001", "env-dev", "TOOL-030", "precision-jig", ToolingAssetStatus.Maintenance),
                CreateTooling("org-001", "env-dev", "TOOL-005", "other-type", ToolingAssetStatus.Maintenance),
                CreateTooling("org-001", "env-dev", "TOOL-015", "precision-jig", ToolingAssetStatus.Available),
                CreateTooling("org-001", "env-dev", "TOOL-025", "precision-jig", ToolingAssetStatus.Retired),
                CreateTooling("org-002", "env-dev", "TOOL-001", "precision-jig", ToolingAssetStatus.Maintenance),
                CreateTooling("org-001", "env-prod", "TOOL-002", "precision-jig", ToolingAssetStatus.Maintenance));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tooling-http-test-token");

        var response = await client.GetAsync(
            "/api/business/v1/master-data/tooling-assets" +
            "?organizationId=org-001&environmentId=env-dev" +
            "&keyword=precision-jig&status=maintenance&skip=1&take=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(["TOOL-020", "TOOL-030"], items.Select(item => item.GetProperty("code").GetString()));
        Assert.All(items, item =>
        {
            Assert.Equal(JsonValueKind.String, item.GetProperty("status").ValueKind);
            Assert.Equal("maintenance", item.GetProperty("status").GetString());
            Assert.False(item.GetProperty("isSchedulable").GetBoolean());
        });

        await AssertStatusWireValueAsync(client, ToolingAssetStatus.Available, "available");
        await AssertStatusWireValueAsync(client, ToolingAssetStatus.Retired, "retired");
    }

    private static async Task AssertStatusWireValueAsync(
        HttpClient client,
        ToolingAssetStatus status,
        string expectedWireValue)
    {
        var response = await client.GetAsync(
            "/api/business/v1/master-data/tooling-assets" +
            "?organizationId=org-001&environmentId=env-dev" +
            $"&keyword=precision-jig&status={expectedWireValue}&skip=0&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.String, item.GetProperty("status").ValueKind);
        Assert.Equal(expectedWireValue, item.GetProperty("status").GetString());
        Assert.Equal(status == ToolingAssetStatus.Available, item.GetProperty("isSchedulable").GetBoolean());
    }

    private static ToolingAsset CreateTooling(
        string organizationId,
        string environmentId,
        string code,
        string toolingType,
        ToolingAssetStatus status)
    {
        var tooling = ToolingAsset.Register(
            organizationId,
            environmentId,
            code,
            $"{code} 工装",
            toolingType,
            ["WC-01"],
            ["SKU-A"],
            null);
        if (status != ToolingAssetStatus.Available)
        {
            tooling.ChangeStatus(status, "HTTP 查询夹具状态");
        }

        return tooling;
    }

    private sealed class ToolingAssetDirectoryHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"masterdata-tooling-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "tooling-http-test-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .UseInternalServiceProvider(efServices)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
