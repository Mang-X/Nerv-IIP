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
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MasterDataResourceListHttpTests
{
    [Fact]
    public async Task Get_resources_normalizes_tenant_and_keyword_and_keeps_legacy_page_clamping()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-PUMP", "Pump", "pcs", "finished-goods"));
            dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-OTHER", "Other", "pcs", "finished-goods"));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "master-data-resource-list-http-test-token");

        foreach (var paging in new[] { "skip=-1&take=0", "skip=0&take=501", "skip=0&take=100" })
        {
            var response = await client.GetAsync(
                "/api/business/v1/master-data/resources" +
                "?organizationId=%20org-001%20&environmentId=%20env-dev%20&resourceType=sku" +
                "&keyword=%20%20pump%20%20&" + paging);

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            Assert.True(document.RootElement.TryGetProperty("data", out var data), body);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, data.GetProperty("total").GetInt32());
            Assert.Equal("SKU-PUMP", Assert.Single(data.GetProperty("resources").EnumerateArray()).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Get_resources_accepts_existing_tenant_identifier_length()
    {
        var organizationId = "org-" + "x".PadRight(61, 'x');

        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Skus.Add(Sku.Create(organizationId, "env-dev", "SKU-LONG-TENANT", "Long tenant", "pcs", "finished-goods"));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "master-data-resource-list-http-test-token");

        var response = await client.GetAsync(
            $"/api/business/v1/master-data/resources?organizationId={organizationId}&environmentId=env-dev&resourceType=sku");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("?environmentId=env-dev&resourceType=sku", "组织标识不能为空")]
    [InlineData("?organizationId=org-001&resourceType=sku", "环境标识不能为空")]
    public async Task Get_resources_without_tenant_value_returns_response_data_error(string query, string expectedMessage)
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "master-data-resource-list-http-test-token");

        var response = await client.GetAsync("/api/business/v1/master-data/resources" + query);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(expectedMessage, document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        var errorData = document.RootElement.GetProperty("errorData");
        Assert.Equal(JsonValueKind.Array, errorData.ValueKind);
        Assert.NotEmpty(errorData.EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("data", out _), body);
    }

    private sealed class MasterDataResourceListHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"master-data-resource-list-http-{Guid.CreateVersion7():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "master-data-resource-list-http-test-token");
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
