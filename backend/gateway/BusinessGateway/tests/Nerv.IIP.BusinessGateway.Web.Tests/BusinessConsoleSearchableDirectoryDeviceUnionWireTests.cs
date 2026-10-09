using System.Net;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// #4257 DomainInvariant/Regression：正式 Gateway + HTTP client，owner 在此组用 HTTP stub。
// PostgreSQL 与真 OEE 页面另行验收；本组不声称真实服务证明。
public sealed partial class BusinessConsoleSearchableDirectoryWireTests
{
    [Fact]
    public async Task Equipment_directory_forwards_permission_aware_spatial_union_and_returns_real_device_identity()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("site", "SITE-A", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("workshop", "WS-B", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("production-line", "LINE-C", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("work-center", "WC-D", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("self", "user-001", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("team", "TEAM-A", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("site", "SITE-DENIED", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var downstream = new JsonHandler("""
            {"resources":[{"resourceType":"device-asset","code":"DEV-B","displayName":"设备 B","active":true,
            "snapshotVersion":"v1","deviceAssetId":"018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9"}],"total":21}
            """);
        var masterData = new HttpBusinessMasterDataClient(new HttpClient(downstream) { BaseAddress = new Uri("http://master-data.local") });
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.GetAsync(
            "/api/business-console/v1/directories/equipment?organizationId=org-001&environmentId=env-dev&keyword=设备&pageIndex=2&pageSize=20&deviceScopeSiteCodes=FORGED");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["SITE-A"], QueryValues(downstream, "deviceScopeSiteCodes"));
        Assert.Equal(["WS-B"], QueryValues(downstream, "deviceScopeWorkshopCodes"));
        Assert.Equal(["LINE-C"], QueryValues(downstream, "deviceScopeLineCodes"));
        Assert.Equal(["WC-D"], QueryValues(downstream, "deviceScopeWorkCenterCodes"));
        Assert.Equal(["20"], QueryValues(downstream, "skip"));
        Assert.Equal(["20"], QueryValues(downstream, "take"));
        Assert.Equal(["设备"], QueryValues(downstream, "keyword"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("DEV-B", item.GetProperty("code").GetString());
        Assert.Equal("018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9", item.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Equipment_organization_grant_reads_all_devices_without_spatial_narrowing()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.MasterDataResourcesRead, organizationWide: true),
            Grant("self", "user-001", BusinessGatewayPermissions.MasterDataResourcesRead),
        ]);
        var downstream = new JsonHandler("{\"resources\":[],\"total\":0}");
        var masterData = new HttpBusinessMasterDataClient(new HttpClient(downstream) { BaseAddress = new Uri("http://master-data.local") });
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(
            "/api/business-console/v1/directories/equipment?organizationId=org-001&environmentId=env-dev&scopeKind=&scopeId=");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(downstream.RequestUri);
        Assert.Empty(QueryValues(downstream, "deviceScopeSiteCodes"));
        Assert.Empty(QueryValues(downstream, "deviceScopeWorkshopCodes"));
        Assert.Empty(QueryValues(downstream, "deviceScopeLineCodes"));
        Assert.Empty(QueryValues(downstream, "deviceScopeWorkCenterCodes"));
    }

    [Theory]
    [InlineData("site", "SITE-A", "siteCode")]
    [InlineData("workshop", "WS-A", "workshopCode")]
    [InlineData("production-line", "LINE-A", "lineCode")]
    [InlineData("work-center", "WC-A", "workCenterCode")]
    public async Task Equipment_explicit_authorized_scope_is_an_intersection(string kind, string id, string filter)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant(kind, id, BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("work-center", "WC-B", BusinessGatewayPermissions.MasterDataResourcesRead),
        ]);
        var downstream = new JsonHandler("{\"resources\":[],\"total\":0}");
        var masterData = new HttpBusinessMasterDataClient(new HttpClient(downstream) { BaseAddress = new Uri("http://master-data.local") });
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(
            $"/api/business-console/v1/directories/equipment?organizationId=org-001&environmentId=env-dev&scopeKind={kind}&scopeId={id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([id], QueryValues(downstream, filter));
        Assert.Contains("WC-B", QueryValues(downstream, "deviceScopeWorkCenterCodes"));
    }

    [Theory]
    [InlineData("self")]
    [InlineData("team")]
    public async Task Equipment_without_spatial_grants_is_denied(string kind)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [Grant(kind, "USER-OR-TEAM", BusinessGatewayPermissions.MasterDataResourcesRead)]);
        var downstream = new JsonHandler("{\"resources\":[],\"total\":0}");
        var masterData = new HttpBusinessMasterDataClient(new HttpClient(downstream) { BaseAddress = new Uri("http://master-data.local") });
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(
            "/api/business-console/v1/directories/equipment?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.RequestUri);
    }

    [Theory]
    [InlineData("SITE-A", "org-001", "env-dev", HttpStatusCode.OK)]
    [InlineData("SITE-DENIED", "org-001", "env-dev", HttpStatusCode.Forbidden)]
    [InlineData("SITE-A", "other-org", "env-dev", HttpStatusCode.Forbidden)]
    [InlineData("SITE-A", "org-001", "other-env", HttpStatusCode.Forbidden)]
    public async Task Equipment_child_scope_requires_authorized_master_data_ownership(
        string site, string organization, string environment, HttpStatusCode expected)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [Grant("site", "SITE-A", BusinessGatewayPermissions.MasterDataResourcesRead)]);
        var masterData = new RecordingMasterDataClient
        {
            ResourceDetailResponse = new("work-center", "WC-A", "工作中心", true, "v1", organization, environment, PlantCode: site),
        };
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(
            "/api/business-console/v1/directories/equipment?organizationId=org-001&environmentId=env-dev&scopeKind=work-center&scopeId=WC-A");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("WC-A", masterData.LastListResourcesRequest!.WorkCenterCode);
        }
        else
        {
            Assert.Equal(0, masterData.ListResourcesCallCount);
        }
    }
}
