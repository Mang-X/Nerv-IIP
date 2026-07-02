using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewaySearchTests
{
    [Fact]
    public async Task Business_console_search_requires_user_authentication()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/business-console/v1/search?q=SKU-001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Business_console_search_filters_denied_sources_and_reports_unsupported_types()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesWorkOrdersRead);
        var masterData = new RecordingMasterDataClient
        {
            Resources =
            [
                new("sku", "SKU-SECRET-001", "Sensitive product name", true, "v1"),
            ],
        };
        var inventory = new RecordingInventoryClient();
        var telemetry = new RecordingIndustrialTelemetryClient();
        var mes = new RecordingMesClient
        {
            WorkOrders =
            [
                new(
                    "WO-271-001",
                    "SKU-271",
                    null,
                    10,
                    1,
                    DateTimeOffset.Parse("2026-06-03T08:00:00Z"),
                    "released",
                    []),
            ],
        };
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/search?q=271&types=masterDataSku,mesWorkOrder,inventoryLot,equipmentAlarm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sensitive", body, StringComparison.Ordinal);
        Assert.Equal(0, masterData.ListResourcesCallCount);
        Assert.Equal(0, inventory.AvailabilityCallCount);
        Assert.Equal(0, telemetry.CurrentStateCallCount);
        Assert.Equal(1, mes.WorkOrderListCallCount);
        Assert.Equal("internal-test-token", mes.LastInternalToken);

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("source-window", data.GetProperty("matchScope").GetString());
        Assert.Contains("first requested result window", data.GetProperty("matchScopeDescription").GetString(), StringComparison.Ordinal);
        var result = Assert.Single(data.GetProperty("results").EnumerateArray());
        Assert.Equal("mesWorkOrder", result.GetProperty("objectType").GetString());
        Assert.Equal("WO-271-001", result.GetProperty("objectNumber").GetString());
        AssertTypeStatus(data, "masterDataSku", "forbidden");
        AssertTypeStatus(data, "mesWorkOrder", "available");
        AssertTypeStatus(data, "inventoryLot", "unsupported");
        AssertTypeStatus(data, "equipmentAlarm", "forbidden");
    }

    [Fact]
    public async Task Business_console_search_applies_global_result_take_limit()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.MasterDataProductsRead,
            BusinessGatewayPermissions.MesWorkOrdersRead);
        var masterData = new RecordingMasterDataClient
        {
            Resources =
            [
                new("sku", "SKU-271-001", "Search Item 1", true, "v1"),
                new("sku", "SKU-271-002", "Search Item 2", true, "v1"),
                new("sku", "SKU-271-003", "Search Item 3", true, "v1"),
                new("sku", "SKU-271-004", "Search Item 4", true, "v1"),
            ],
        };
        var mes = new RecordingMesClient
        {
            WorkOrders =
            [
                new("WO-271-001", "SKU-271", null, 10, 1, DateTimeOffset.Parse("2026-06-03T08:00:00Z"), "released", []),
                new("WO-271-002", "SKU-271", null, 20, 2, DateTimeOffset.Parse("2026-06-04T08:00:00Z"), "planned", []),
                new("WO-271-003", "SKU-271", null, 30, 3, DateTimeOffset.Parse("2026-06-05T08:00:00Z"), "planned", []),
            ],
        };
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/search?q=271&types=masterDataSku,mesWorkOrder&take=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("take").GetInt32());
        Assert.Equal(3, data.GetProperty("results").EnumerateArray().Count());
        Assert.True(masterData.LastListResourcesRequest!.Take <= 50);
        Assert.True(mes.LastWorkOrderListRequest!.Take <= 50);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Business_console_search_returns_forbidden_when_authenticated_user_is_missing_scope_claims(
        bool includeOrganizationId,
        bool includeEnvironmentId)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            BusinessGatewayTestTokens.ValidAccessToken(
                includeOrganizationId: includeOrganizationId,
                includeEnvironmentId: includeEnvironmentId));

        var response = await client.GetAsync("/api/business-console/v1/search?q=SKU-001");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(403, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Forbidden.", document.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("", 10, "q")]
    [InlineData("   ", 10, "q")]
    [InlineData("SKU-001", -1, "take")]
    [InlineData("SKU-001", 51, "take")]
    public async Task Business_console_search_returns_bad_request_on_invalid_request(
        string query,
        int take,
        string expectedInvalidField)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            $"/api/business-console/v1/search?q={WebUtility.UrlEncode(query)}&take={take}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedInvalidField, body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, auth.CallCount);
    }

    private static void AssertTypeStatus(JsonElement data, string objectType, string status)
    {
        var item = data.GetProperty("typeStatuses")
            .EnumerateArray()
            .Single(typeStatus => typeStatus.GetProperty("objectType").GetString() == objectType);
        Assert.Equal(status, item.GetProperty("status").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            BusinessGatewayTestServiceBaseUrls.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}
