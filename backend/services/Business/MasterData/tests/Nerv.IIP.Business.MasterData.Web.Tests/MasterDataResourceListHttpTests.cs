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
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MasterDataResourceListHttpTests
{
    // #4257 DomainInvariant/Regression：授权 OR 谓词必须在 total、排序、分页前应用。
    // 本宿主使用 EF InMemory；不据此宣称 PostgreSQL SQL 翻译已验证。
    [Fact]
    public async Task Device_spatial_union_filters_before_count_sort_and_page_without_duplicates()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DeviceAsset Device(string code, string site, string workshop, string line, string center,
                string organization = "org-001", string environment = "env-dev")
            {
                var device = DeviceAsset.Register(organization, environment, code, "设备", line, center);
                device.UpdateLedger(null, null, "", null, "", site, workshop, line, "", null, null);
                return device;
            }
            db.DeviceAssets.AddRange(
                Device("00-DENIED", "SITE-X", "WS-X", "LINE-X", "WC-X"),
                Device("10-SITE", "SITE-A", "WS-X", "LINE-X", "WC-X"),
                Device("20-WORKSHOP", "SITE-X", "WS-B", "LINE-X", "WC-X"),
                Device("30-LINE", "SITE-X", "WS-X", "LINE-C", "WC-X"),
                Device("40-CENTER", "SITE-X", "WS-X", "LINE-X", "WC-D"),
                Device("50-OVERLAP", "SITE-A", "WS-B", "LINE-C", "WC-D"),
                Device("60-OTHER-ORG", "SITE-A", "WS-B", "LINE-C", "WC-D", organization: "other-org"),
                Device("70-OTHER-ENV", "SITE-A", "WS-B", "LINE-C", "WC-D", environment: "other-env"));
            await db.SaveChangesAsync();
        }
        using var client = CreateAuthenticatedClient(factory);
        const string url = "/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=device-asset"
            + "&deviceScopeSiteCodes=SITE-A&deviceScopeWorkshopCodes=WS-B&deviceScopeLineCodes=LINE-C&deviceScopeWorkCenterCodes=WC-D";
        var codes = new List<string>();
        for (var skip = 0; skip < 5; skip += 2)
        {
            using var response = await client.GetAsync($"{url}&skip={skip}&take=2");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(5, data.GetProperty("total").GetInt32());
            codes.AddRange(data.GetProperty("resources").EnumerateArray().Select(x => x.GetProperty("code").GetString()!));
        }
        Assert.Equal(["10-SITE", "20-WORKSHOP", "30-LINE", "40-CENTER", "50-OVERLAP"], codes);
        foreach (var (filter, expected) in new[]
        {
            ("siteCode=SITE-A", new[] { "10-SITE", "50-OVERLAP" }),
            ("workshopCode=WS-B", new[] { "20-WORKSHOP", "50-OVERLAP" }),
            ("lineCode=LINE-C", new[] { "30-LINE", "50-OVERLAP" }),
            ("workCenterCode=WC-D", new[] { "40-CENTER", "50-OVERLAP" }),
            ("keyword=OVERLAP", new[] { "50-OVERLAP" }),
        })
        {
            using var response = await client.GetAsync($"{url}&{filter}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(expected.Length, data.GetProperty("total").GetInt32());
            Assert.Equal(expected, data.GetProperty("resources").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToArray());
        }
    }

    [Fact]
    public async Task Get_device_resources_resolves_exact_id_or_code_before_paging_and_returns_canonical_id()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        DeviceAsset target;
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.DeviceAssets.AddRange(Enumerable.Range(0, 101)
                .Select(index => DeviceAsset.Register(
                    "org-001",
                    "env-dev",
                    $"DEV-{index:D3}",
                    $"Device {index:D3}",
                    "LINE-001",
                    "WC-001")));
            target = DeviceAsset.Register("org-001", "env-dev", "ZZZ-TARGET", "Target", "LINE-001", "WC-001");
            dbContext.DeviceAssets.AddRange(
                target,
                DeviceAsset.Register("org-001", "env-dev", "ZZZ-TARGET-EXTRA", "Prefix collision", "LINE-001", "WC-001"),
                DeviceAsset.Register("org-002", "env-dev", "ZZZ-TARGET", "Other organization", "LINE-001", "WC-001"),
                DeviceAsset.Register("org-001", "env-test", "ZZZ-TARGET", "Other environment", "LINE-001", "WC-001"));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        foreach (var reference in new[] { target.Id.ToString(), target.Code })
        {
            var response = await client.GetAsync(
                "/api/business/v1/master-data/resources" +
                $"?organizationId=org-001&environmentId=env-dev&resourceType=device-asset&deviceAssetId={reference}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("total").GetInt32());
            var resource = Assert.Single(data.GetProperty("resources").EnumerateArray().ToArray());
            Assert.Equal("ZZZ-TARGET", resource.GetProperty("code").GetString());
            Assert.NotEqual(resource.GetProperty("code").GetString(), resource.GetProperty("deviceAssetId").GetString());
            Assert.Equal(target.Id.ToString(), resource.GetProperty("deviceAssetId").GetString());
        }

        var missingResponse = await client.GetAsync(
            "/api/business/v1/master-data/resources" +
            "?organizationId=org-001&environmentId=env-dev&resourceType=device-asset&deviceAssetId=DEV-MISSING");
        var missingBody = await missingResponse.Content.ReadAsStringAsync();
        Assert.True(missingResponse.IsSuccessStatusCode, $"{missingResponse.StatusCode}: {missingBody}");
        using var missingDocument = JsonDocument.Parse(missingBody);
        Assert.Equal(0, missingDocument.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Empty(missingDocument.RootElement.GetProperty("data").GetProperty("resources").EnumerateArray());
    }

    [Fact]
    public async Task Get_device_resources_rejects_id_code_namespace_collision_as_ambiguous()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        string ambiguousReference;
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deviceById = DeviceAsset.Register("org-001", "env-dev", "DEV-B", "Device B", "LINE-B", "WC-B");
            dbContext.DeviceAssets.Add(deviceById);
            await dbContext.SaveChangesAsync();
            ambiguousReference = deviceById.Id.ToString();
            dbContext.DeviceAssets.Add(DeviceAsset.Register(
                "org-001",
                "env-dev",
                ambiguousReference,
                "Device A",
                "LINE-A",
                "WC-A"));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync(
            "/api/business/v1/master-data/resources" +
            $"?organizationId=org-001&environmentId=env-dev&resourceType=device-asset&deviceAssetId={ambiguousReference}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("无法唯一确定", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(document.RootElement.TryGetProperty("data", out _), body);
    }

    [Fact]
    public async Task Get_device_resources_rejects_alias_closure_collision_as_ambiguous()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var requestedDevice = DeviceAsset.Register("org-001", "env-dev", "DEV-A", "Device A", "LINE-A", "WC-A");
            dbContext.DeviceAssets.Add(requestedDevice);
            await dbContext.SaveChangesAsync();
            dbContext.DeviceAssets.Add(DeviceAsset.Register(
                "org-001",
                "env-dev",
                requestedDevice.Id.ToString(),
                "Device B",
                "LINE-B",
                "WC-B"));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync(
            "/api/business/v1/master-data/resources" +
            "?organizationId=org-001&environmentId=env-dev&resourceType=device-asset&deviceAssetId=DEV-A");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("无法唯一确定", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(document.RootElement.TryGetProperty("data", out _), body);
    }

    [Fact]
    public async Task Get_shift_resources_applies_exact_shift_code_before_paging()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Shifts.AddRange(
                Shift.Create("org-001", "env-dev", "SHIFT-DAY", "Day", new TimeOnly(8, 0), new TimeOnly(16, 0), 480),
                Shift.Create("org-001", "env-dev", "SHIFT-NIGHT", "Night", new TimeOnly(20, 0), new TimeOnly(4, 0), 480),
                Shift.Create("org-001", "env-dev", "SHIFT-NIGHT-EXTRA", "Night extra", new TimeOnly(21, 0), new TimeOnly(5, 0), 480));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync(
            "/api/business/v1/master-data/resources" +
            "?organizationId=org-001&environmentId=env-dev&resourceType=shift&shiftCode=SHIFT-NIGHT&take=1");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        var resource = Assert.Single(data.GetProperty("resources").EnumerateArray().ToArray());
        Assert.Equal("SHIFT-NIGHT", resource.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_product_categories_uses_composed_criteria_for_tenant_keyword_and_page()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ProductCategories.AddRange(
                ProductCategory.Create("org-001", "env-dev", "CAT-PUMP-A", "Pump A", null, null),
                ProductCategory.Create("org-001", "env-dev", "CAT-PUMP-B", "Pump B", null, null),
                ProductCategory.Create("org-001", "env-dev", "CAT-OTHER", "Other", null, null),
                ProductCategory.Create("org-002", "env-dev", "CAT-PUMP-OTHER-ORG", "Pump other org", null, null),
                ProductCategory.Create("org-001", "env-test", "CAT-PUMP-OTHER-ENV", "Pump other env", null, null));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync(
            "/api/business/v1/master-data/product-categories" +
            "?organizationId=%20org-001%20&environmentId=%20env-dev%20&search=%20PuMp%20&skip=1&take=1");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("CAT-PUMP-B", items[0].GetProperty("categoryCode").GetString());
        var categoryCodes = items.Select(item => item.GetProperty("categoryCode").GetString()).ToArray();
        Assert.DoesNotContain("CAT-PUMP-OTHER-ORG", categoryCodes);
        Assert.DoesNotContain("CAT-PUMP-OTHER-ENV", categoryCodes);
    }

    [Fact]
    public async Task Get_skills_uses_composed_criteria_for_tenant_keyword_and_page()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Skills.AddRange(
                Skill.Create("org-001", "env-dev", "SK-PUMP-A", "Pump A", "Manufacturing", false, null, null),
                Skill.Create("org-001", "env-dev", "SK-PUMP-B", "Pump B", "Manufacturing", false, null, null),
                Skill.Create("org-001", "env-dev", "SK-OTHER", "Other", "Manufacturing", false, null, null),
                Skill.Create("org-002", "env-dev", "SK-PUMP-OTHER-ORG", "Pump other org", "Manufacturing", false, null, null),
                Skill.Create("org-001", "env-test", "SK-PUMP-OTHER-ENV", "Pump other env", "Manufacturing", false, null, null));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync(
            "/api/business/v1/master-data/skills" +
            "?organizationId=%20org-001%20&environmentId=%20env-dev%20&search=%20PuMp%20&skip=1&take=1");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("SK-PUMP-B", items[0].GetProperty("skillCode").GetString());
        var skillCodes = items.Select(item => item.GetProperty("skillCode").GetString()).ToArray();
        Assert.DoesNotContain("SK-PUMP-OTHER-ORG", skillCodes);
        Assert.DoesNotContain("SK-PUMP-OTHER-ENV", skillCodes);
    }

    [Fact]
    public async Task Get_resources_normalizes_tenant_and_keyword_and_keeps_legacy_page_clamping()
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-PUMP", "Pump", "pcs", "finished-goods"));
            dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-PUMP-2", "Pump spare", "pcs", "finished-goods"));
            dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-OTHER", "Other", "pcs", "finished-goods"));
            dbContext.Skus.Add(Sku.Create("org-002", "env-dev", "SKU-PUMP-OTHER-ORG", "Pump other org", "pcs", "finished-goods"));
            dbContext.Skus.Add(Sku.Create("org-001", "env-test", "SKU-PUMP-OTHER-ENV", "Pump other env", "pcs", "finished-goods"));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "master-data-resource-list-http-test-token");

        var pagingCases = new[]
        {
            (Paging: "skip=-1&take=0", ExpectedCount: 1, ExpectedFirstCode: "SKU-PUMP"),
            (Paging: "skip=0&take=501", ExpectedCount: 2, ExpectedFirstCode: "SKU-PUMP"),
            (Paging: "skip=1&take=1", ExpectedCount: 1, ExpectedFirstCode: "SKU-PUMP-2"),
            (Paging: "skip=0&take=100", ExpectedCount: 2, ExpectedFirstCode: "SKU-PUMP"),
        };

        foreach (var paging in pagingCases)
        {
            var response = await client.GetAsync(
                "/api/business/v1/master-data/resources" +
                "?organizationId=%20org-001%20&environmentId=%20env-dev%20&resourceType=sku" +
                "&keyword=%20%20PuMp%20%20&" + paging.Paging);

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            Assert.True(document.RootElement.TryGetProperty("data", out var data), body);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2, data.GetProperty("total").GetInt32());
            var resources = data.GetProperty("resources").EnumerateArray().ToArray();
            Assert.Equal(paging.ExpectedCount, resources.Length);
            Assert.Equal(paging.ExpectedFirstCode, resources[0].GetProperty("code").GetString());
            var resourceCodes = resources.Select(resource => resource.GetProperty("code").GetString()).ToArray();
            Assert.DoesNotContain("SKU-PUMP-OTHER-ORG", resourceCodes);
            Assert.DoesNotContain("SKU-PUMP-OTHER-ENV", resourceCodes);
        }
    }

    // #3825：工作中心、车间目录按网关给出的授权工厂集合收窄（重复参数 siteCodes），集合外的工厂一条都不返回。
    [Theory]
    [InlineData("workshop")]
    [InlineData("work-center")]
    public async Task Get_resources_narrows_to_the_given_site_set(string resourceType)
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var site in new[] { "SITE-A", "SITE-B", "SITE-C" })
            {
                dbContext.Workshops.Add(Workshop.Create("org-001", "env-dev", $"WS-{site}", $"车间 {site}", site, null, null));
                dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                    "org-001", "env-dev", $"WC-{site}", $"工作中心 {site}", 480, "work-center",
                    site, "LINE-1", "STANDARD", "minute", true));
            }

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync(
            "/api/business/v1/master-data/resources" +
            $"?organizationId=org-001&environmentId=env-dev&resourceType={resourceType}&siteCodes=SITE-A&siteCodes=SITE-C");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        var prefix = resourceType == "workshop" ? "WS" : "WC";
        Assert.Equal(
            [$"{prefix}-SITE-A", $"{prefix}-SITE-C"],
            data.GetProperty("resources").EnumerateArray().Select(resource => resource.GetProperty("code").GetString()!).ToArray());
        Assert.Equal(2, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Get_resources_accepts_existing_tenant_identifier_length()
    {
        var organizationId = "org-" + "x".PadRight(61, 'x');
        var environmentId = "env-" + "y".PadRight(61, 'y');

        await using var factory = new MasterDataResourceListHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Skus.Add(Sku.Create(organizationId, environmentId, "SKU-LONG-TENANT", "Long tenant", "pcs", "finished-goods"));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "master-data-resource-list-http-test-token");

        var response = await client.GetAsync(
            $"/api/business/v1/master-data/resources?organizationId={organizationId}&environmentId={environmentId}&resourceType=sku");

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

    [Theory]
    [InlineData("product-categories", "?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("product-categories", "?organizationId=org-001", "环境标识不能为空")]
    [InlineData("skills", "?environmentId=env-dev", "组织标识不能为空")]
    [InlineData("skills", "?organizationId=org-001", "环境标识不能为空")]
    public async Task Get_product_categories_and_skills_without_tenant_value_returns_response_data_error(
        string resource,
        string query,
        string expectedMessage)
    {
        await using var factory = new MasterDataResourceListHttpTestFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync($"/api/business/v1/master-data/{resource}{query}");

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

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "master-data-resource-list-http-test-token");
        return client;
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
