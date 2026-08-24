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
                CreateTooling("org-002", "env-dev", "TOOL-001", "precision-jig", ToolingAssetStatus.Maintenance),
                CreateTooling("org-001", "env-prod", "TOOL-002", "precision-jig", ToolingAssetStatus.Maintenance));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tooling-http-test-token");

        var response = await client.GetAsync(
            "/api/business/v1/master-data/tooling-assets" +
            "?organizationId=org-001&environmentId=env-dev" +
            "&keyword=precision-jig&status=maintenance&skip=1&take=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("TOOL-020", item.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("status").ValueKind);
        Assert.Equal("maintenance", item.GetProperty("status").GetString());
        Assert.False(item.GetProperty("isSchedulable").GetBoolean());
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
