using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayWmsTests
{
    [Fact]
    public async Task Inbound_orders_include_inventory_context_in_single_facade_response()
    {
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal("internal-test-token", inventory.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsListRequest("org-001", "env-dev"), wms.LastInboundRequest);
        Assert.Equal("SKU-001", inventory.LastAvailabilityRequest!.SkuCode);
        Assert.Equal("S1", inventory.LastAvailabilityRequest.SiteCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("available", data.GetProperty("sourceStatus").GetString());
        Assert.Equal("BusinessInventory", data.GetProperty("inventoryContext").GetProperty("source").GetString());
        Assert.Equal(8, data.GetProperty("inventoryContext").GetProperty("availableQuantity").GetDecimal());
        Assert.Equal("IN-001", data.GetProperty("items")[0].GetProperty("inboundOrderNo").GetString());
    }

    [Fact]
    public async Task Outbound_orders_use_shipments_permission_and_internal_service_token()
    {
        var wms = new RecordingWmsClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsListRequest("org-001", "env-dev"), wms.LastOutboundRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("OUT-001", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("outboundOrderNo").GetString());
    }

    [Fact]
    public async Task Wcs_tasks_use_automation_permission_and_filters()
    {
        var wms = new RecordingWmsClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/wcs-tasks?organizationId=org-001&environmentId=env-dev&externalTaskId=EXT-001&warehouseTaskId=warehouse-task-001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsWcsTaskListRequest("org-001", "env-dev", "EXT-001", "warehouse-task-001"), wms.LastWcsTaskRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("EXT-001", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("externalTaskId").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
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

internal sealed class RecordingWmsClient : IBusinessWmsClient
{
    public string? LastInternalToken { get; private set; }

    public BusinessConsoleWmsListRequest? LastInboundRequest { get; private set; }

    public BusinessConsoleWmsListRequest? LastOutboundRequest { get; private set; }

    public BusinessConsoleWmsWcsTaskListRequest? LastWcsTaskRequest { get; private set; }

    public Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastInboundRequest = request;
        return Task.FromResult(new BusinessConsoleWmsInboundOrderListResponse(
        [
            new BusinessConsoleWmsInboundOrderItem(
                "inbound-order-001",
                "IN-001",
                "Created",
                DateTime.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)),
        ],
        null,
        "unsupported"));
    }

    public Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastOutboundRequest = request;
        return Task.FromResult(new BusinessConsoleWmsOutboundOrderListResponse(
        [
            new BusinessConsoleWmsOutboundOrderItem(
                "outbound-order-001",
                "OUT-001",
                "Created",
                DateTime.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)),
        ]));
    }

    public Task<BusinessConsoleWmsWcsTaskListResponse> ListWcsTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWcsTaskListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastWcsTaskRequest = request;
        return Task.FromResult(new BusinessConsoleWmsWcsTaskListResponse(
        [
            new BusinessConsoleWmsWcsTaskItem(
                "wcs-task-001",
                "org-001",
                "env-dev",
                "warehouse-task-001",
                "demo",
                "EXT-001",
                "Dispatched",
                1,
                null,
                null,
                DateTime.Parse("2026-06-01T10:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                null,
                null),
        ]));
    }
}
