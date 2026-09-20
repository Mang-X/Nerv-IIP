using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

// #2130：经公开 HTTP 绑定、真实 Query/validator/handler 验证分页与租户合同。
[Trait("Category", "PublicContract")]
[Trait("Category", "Regression")]
public sealed class InventoryDirectoryHttpContractTests
{
    [Theory]
    [InlineData("", 50, "LOC-000")]
    [InlineData("&skip=0&take=1", 1, "LOC-000")]
    [InlineData("&skip=1&take=200", 50, "LOC-001")]
    public async Task Directory_preserves_default_and_boundary_paging_with_blank_keyword(
        string paging, int count, string firstCode)
    {
        await using var factory = CreateFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 51; index++)
            {
                db.StockLocations.Add(StockLocation.CreateOrUpdate(
                    null, "org-directory", "env-directory", $"LOC-{index:D3}", "bin", "SITE-A", null, "active"));
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(factory);
        using var response = await client.GetAsync(
            "/api/inventory/v1/directory?directoryType=location&organizationId=org-directory&environmentId=env-directory&keyword=%20%20" + paging);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(51, data.GetProperty("total").GetInt32());
        var items = data.GetProperty("items");
        Assert.Equal(count, items.GetArrayLength());
        Assert.Equal(firstCode, items[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("organizationId=org-directory&environmentId=env-directory&skip=-1", "skip")]
    [InlineData("organizationId=org-directory&environmentId=env-directory&take=0", "take")]
    [InlineData("organizationId=org-directory&environmentId=env-directory&take=201", "take")]
    [InlineData("environmentId=env-directory", "组织标识不能为空。")]
    [InlineData("organizationId=org-directory", "环境标识不能为空。")]
    public async Task Directory_rejects_invalid_paging_and_missing_tenant_through_error_envelope(
        string query, string field)
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/inventory/v1/directory?directoryType=location&" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(field, body.RootElement.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(99999, body.RootElement.GetProperty("code").GetInt32());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databaseName = $"directory-http-{Guid.NewGuid():N}";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        return client;
    }
}
