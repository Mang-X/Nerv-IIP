using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleSearchableDirectoryWireTests
{
    [Fact]
    public async Task Directory_endpoint_authorizes_only_owner_permission_and_forwards_site_scope()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.InventoryLedgerRead);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        await using var factory = CreateFactory(auth, downstream);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&scopeKind=site&scopeId=SITE-A");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.InventoryLedgerRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("site", auth.LastRequirement.ResourceType);
        Assert.Equal("SITE-A", auth.LastRequirement.ResourceId);
        Assert.Contains("siteCode=SITE-A", downstream.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cross_owner_permission_and_invalid_scope_fail_before_downstream()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MasterDataResourcesRead);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        await using var factory = CreateFactory(auth, downstream);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var forbidden = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");
        var invalidScope = await client.GetAsync(
            "/api/business-console/v1/directories/priority?organizationId=org-001&environmentId=env-dev&scopeKind=site&scopeId=SITE-A");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidScope.StatusCode);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Unknown_ranking_mode_is_rejected_without_authorization_or_downstream_call()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        await using var factory = CreateFactory(auth, downstream);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&rankingMode=personalized");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Blank_tenant_scope_is_rejected_without_authorization_or_downstream_call()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        await using var factory = CreateFactory(auth, downstream);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Station_route_preserves_scoped_stable_id_and_readable_code()
    {
        const string stableId = "station:7:org-0017:env-dev8:SITE-0016:WS-0018:LINE-0016:WC-0016:ST-001";
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MasterDataResourcesRead);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        var masterData = new RecordingMasterDataClient
        {
            Resources =
            [
                new BusinessConsoleResourceItem(
                    "station",
                    stableId,
                    "ST-001",
                    true,
                    "v1",
                    SiteCode: "SITE-001",
                    LineCode: "LINE-001",
                    WorkshopCode: "WS-001",
                    WorkCenterCode: "WC-001",
                    StationCode: "ST-001"),
            ],
        };
        await using var factory = CreateFactory(auth, downstream, masterData);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/station?organizationId=org-001&environmentId=env-dev&scopeKind=work-center&scopeId=WC-001&pageIndex=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = body.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(stableId, item.GetProperty("id").GetString());
        Assert.Equal("ST-001", item.GetProperty("code").GetString());
        Assert.Equal("WC-001", item.GetProperty("context").GetProperty("workCenterCode").GetString());
        Assert.Equal("station", masterData.LastListResourcesRequest!.ResourceType);
        Assert.Equal("WC-001", masterData.LastListResourcesRequest.WorkCenterCode);
    }

    [Theory]
    [InlineData("{\"status\":\"available\",\"reasonCode\":null,\"items\":[{\"id\":\"SKU-1:LOT-1\",\"code\":\"LOT-1\",\"displayName\":\"LOT-1 · SKU-1\",\"sourceService\":\"inventory\",\"siteCode\":\"SITE-A\",\"skuCode\":\"SKU-1\"}],\"total\":1}")]
    [InlineData("{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{\"status\":\"available\",\"reasonCode\":null,\"items\":[{\"id\":\"SKU-1:LOT-1\",\"code\":\"LOT-1\",\"displayName\":\"LOT-1 · SKU-1\",\"sourceService\":\"inventory\",\"siteCode\":\"SITE-A\",\"skuCode\":\"SKU-1\"}],\"total\":1}}")]
    public async Task Inventory_directory_accepts_raw_and_ResponseData_data_shapes(string payload)
    {
        var handler = new JsonHandler(payload);
        var client = new HttpBusinessInventoryClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://inventory.local") },
            Options.Create(new BusinessGatewayInventoryForwardedPermissionOptions()));

        var response = await client.ListDirectoryAsync(
            "internal-token",
            new BusinessConsoleInventoryDirectoryRequest("org-1", "env-1", "batch", "lot", "SITE-A", "SKU-1"),
            CancellationToken.None);

        Assert.Equal("SKU-1:LOT-1", Assert.Single(response.Items).Id);
        Assert.Contains("directoryType=batch", handler.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("siteCode=SITE-A", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":null}")]
    public async Task Authoritative_directory_wire_failures_fail_closed(string payload)
    {
        var client = new HttpBusinessInventoryClient(
            new HttpClient(new JsonHandler(payload)) { BaseAddress = new Uri("http://inventory.local") },
            Options.Create(new BusinessGatewayInventoryForwardedPermissionOptions()));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ListDirectoryAsync(
            "internal-token",
            new BusinessConsoleInventoryDirectoryRequest("org-1", "env-1", "location"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    [Fact]
    public async Task Maintenance_reason_uses_reason_code_as_stable_id_and_preserves_owner_categories()
    {
        const string payload = "{\"success\":true,\"data\":{\"items\":[{\"downtimeReasonId\":{\"id\":\"01900000-0000-7000-8000-000000000001\"},\"organizationId\":\"org-1\",\"environmentId\":\"env-1\",\"reasonCode\":\"BREAKDOWN\",\"description\":\"Breakdown\",\"reasonCategory\":\"unplanned\",\"lossCategory\":\"availability\"}],\"skip\":0,\"take\":20,\"total\":1}}";
        var client = new HttpBusinessMaintenanceClient(
            new HttpClient(new JsonHandler(payload)) { BaseAddress = new Uri("http://maintenance.local") });

        var response = await client.ListDowntimeReasonsAsync(
            "internal-token",
            new BusinessConsoleMaintenanceReasonDirectoryRequest("org-1", "env-1", "break"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("BREAKDOWN", item.Id);
        Assert.Equal("unplanned", item.ReasonCategory);
        Assert.Equal("availability", item.LossCategory);
    }

    private sealed class JsonHandler(string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IBusinessGatewayAuthorizationClient auth,
        JsonHandler inventoryHandler,
        IBusinessMasterDataClient? masterData = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton(auth);
                services.RemoveAll<IBusinessInventoryClient>();
                services.AddSingleton<IBusinessInventoryClient>(new HttpBusinessInventoryClient(
                    new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory.local") },
                    Options.Create(new BusinessGatewayInventoryForwardedPermissionOptions())));
                if (masterData is not null)
                {
                    services.RemoveAll<IBusinessMasterDataClient>();
                    services.AddSingleton(masterData);
                }

                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-token"));
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}
