using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BarcodeLabelListHttpTests
{
    [Fact]
    public async Task List_rules_http_endpoint_normalizes_scope_keyword_and_page()
    {
        await using var factory = CreateFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.BarcodeRules.AddRange(
                BarcodeRule.Create("org-001", "env-dev", "FG-PUMP-A", "code128", "PUMP-A", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-001", "env-dev", "FG-PUMP-B", "code128", "PUMP-B", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-001", "env-dev", "FG-OTHER", "code128", "OTHER", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-002", "env-dev", "FG-PUMP-OTHER-ORG", "code128", "PUMP-ORG", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-001", "env-test", "FG-PUMP-OTHER-ENV", "code128", "PUMP-ENV", 40, "none", ["work-order"], "active"));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        using var response = await client.GetAsync(
            "/api/business/v1/barcodes/rules" +
            "?organizationId=%20org-001%20&environmentId=%20env-dev%20&keyword=%20pUmP%20&skip=1&take=1");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        var rules = data.GetProperty("rules").EnumerateArray().ToArray();
        Assert.Single(rules);
        Assert.Equal("FG-PUMP-B", rules[0].GetProperty("ruleCode").GetString());
        var ruleCodes = rules.Select(rule => rule.GetProperty("ruleCode").GetString()).ToArray();
        Assert.DoesNotContain("FG-PUMP-OTHER-ORG", ruleCodes);
        Assert.DoesNotContain("FG-PUMP-OTHER-ENV", ruleCodes);
    }

    [Fact]
    public async Task List_rules_http_endpoint_uses_default_page_and_ignores_blank_keyword()
    {
        await using var factory = CreateFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.BarcodeRules.AddRange(
                BarcodeRule.Create("org-001", "env-dev", "FG-PUMP-A", "code128", "PUMP-A", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-001", "env-dev", "FG-PUMP-B", "code128", "PUMP-B", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-001", "env-dev", "FG-OTHER", "code128", "OTHER", 40, "none", ["work-order"], "active"));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        using var response = await client.GetAsync(
            "/api/business/v1/barcodes/rules?organizationId=org-001&environmentId=env-dev&keyword=%20%20");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(3, data.GetProperty("rules").GetArrayLength());
    }

    [Theory]
    [InlineData("skip=-1&take=1")]
    [InlineData("skip=0&take=0")]
    [InlineData("skip=0&take=501")]
    public async Task List_rules_http_endpoint_rejects_legacy_invalid_page_bounds(string paging)
    {
        await using var factory = CreateFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.BarcodeRules.AddRange(
                BarcodeRule.Create("org-001", "env-dev", "FG-PUMP-A", "code128", "PUMP-A", 40, "none", ["work-order"], "active"),
                BarcodeRule.Create("org-001", "env-dev", "FG-PUMP-B", "code128", "PUMP-B", 40, "none", ["work-order"], "active"));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        using var response = await client.GetAsync(
            "/api/business/v1/barcodes/rules?organizationId=org-001&environmentId=env-dev&" + paging);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("errorData").ValueKind);
        Assert.NotEmpty(document.RootElement.GetProperty("errorData").EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("data", out _), body);
    }

    [Theory]
    [InlineData("?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("?organizationId=org-001", "环境标识不能为空")]
    public async Task List_rules_http_endpoint_returns_response_data_error_for_missing_tenant(
        string query,
        string expectedMessage)
    {
        await using var factory = CreateFactory();
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync("/api/business/v1/barcodes/rules" + query);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(expectedMessage, document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("errorData").ValueKind);
        Assert.NotEmpty(document.RootElement.GetProperty("errorData").EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("data", out _), body);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databaseName = $"barcode-label-list-http-{Guid.CreateVersion7():N}";
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_barcode_list_http;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "barcode-label-list-http-test-token",
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.RemoveAll<IIntegrationEventPublisher>();
                    services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "barcode-label-list-http-test-token");
        return client;
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
