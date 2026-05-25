using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayAuthorizationTests
{
    [Fact]
    public async Task Business_console_endpoint_requires_user_authentication()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Business_gateway_authentication_requires_configured_jwt_settings()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Iam:Jwt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_forbidden_when_iam_denies_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayPermissions.MasterDataProductsRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_resource_list_when_permission_is_allowed()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("SKU-001", data.GetProperty("resources")[0].GetProperty("code").GetString());
        Assert.Equal(1, auth.CallCount);
    }

    [Fact]
    public async Task Business_console_endpoint_rejects_context_mismatch_before_permission_check()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken(environmentId: "env-prod"));

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Theory]
    [MemberData(nameof(BusinessConsoleRoutes))]
    public async Task Business_console_routes_return_forbidden_when_iam_denies_permission(
        HttpMethod method,
        string path,
        string expectedPermission)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        using var request = new HttpRequestMessage(method, $"{path}{(path.Contains('?') ? '&' : '?')}organizationId=org-001&environmentId=env-dev")
        {
            Content = method == HttpMethod.Post
                ? JsonContent.Create(ValidPostBody(path))
                : null
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(expectedPermission, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    private static object ValidPostBody(string path) => path switch
    {
        "/api/business-console/v1/master-data/skus" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "SKU-001",
            name = "Demo SKU",
            baseUomCode = "EA",
            category = "finished-good",
            materialType = "standard",
            batchTrackingPolicy = "none",
            serialTrackingPolicy = "none",
            shelfLifePolicyCode = "none",
            storageConditionCode = "ambient",
            defaultBarcodeRuleCode = "default",
            qualityRequired = true,
            complianceTags = Array.Empty<string>(),
        },
        "/api/business-console/v1/inventory/count-tasks/count-001/adjustments" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            countedQuantity = 1,
            idempotencyKey = "idem-001",
        },
        _ => new { organizationId = "org-001", environmentId = "env-dev" },
    };

    public static TheoryData<HttpMethod, string, string> BusinessConsoleRoutes()
    {
        var routes = new TheoryData<HttpMethod, string, string>();
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/resources", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/skus", BusinessGatewayPermissions.MasterDataProductsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/skus", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/inventory/availability", BusinessGatewayPermissions.InventoryLedgerRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/movements", BusinessGatewayPermissions.InventoryMovementsCreate);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/count-tasks", BusinessGatewayPermissions.InventoryCountsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/count-tasks/count-001/adjustments", BusinessGatewayPermissions.InventoryCountsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/inspection-plans", BusinessGatewayPermissions.QualityInspectionRecordsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/inspection-records", BusinessGatewayPermissions.QualityInspectionRecordsCreate);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/ncrs", BusinessGatewayPermissions.QualityNcrRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/ncrs/ncr-001/disposition", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/ncrs/ncr-001/close", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/mes/work-orders", BusinessGatewayPermissions.MesWorkOrdersRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/work-orders/rush", BusinessGatewayPermissions.MesWorkOrdersManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/schedules/run", BusinessGatewayPermissions.MesSchedulesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/production-reports", BusinessGatewayPermissions.MesReportingWrite);
        return routes;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}

internal sealed class FakeBusinessGatewayAuthorizationClient(bool allowed) : IBusinessGatewayAuthorizationClient
{
    public int CallCount { get; private set; }

    public BusinessGatewayPermissionRequirement? LastRequirement { get; private set; }

    public static FakeBusinessGatewayAuthorizationClient Allowed() => new(true);

    public static FakeBusinessGatewayAuthorizationClient Forbidden() => new(false);

    public Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequirement = requirement;
        return Task.FromResult(allowed
            ? BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
            : BusinessGatewayAuthorizationResult.Forbidden("forbidden"));
    }
}
