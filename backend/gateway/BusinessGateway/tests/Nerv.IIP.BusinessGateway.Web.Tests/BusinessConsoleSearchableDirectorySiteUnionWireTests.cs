using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// #3825：工作中心、车间目录按授权工厂并集收窄的用例；宿主、授权、JsonHandler 等共用部分见同名 partial 主文件。
public sealed partial class BusinessConsoleSearchableDirectoryWireTests
{
    // #3825：工作中心、车间同样按工厂切分，与库存目录同一套并集收窄（#3843）。
    // 过去多个工厂授权、或工厂授权之外再带 self / 工作中心授权，整个目录都 403。
    [Theory]
    [InlineData("work-center", new[] { "site:SITE-A", "site:SITE-B" }, new[] { "SITE-A", "SITE-B" })]
    [InlineData("workshop", new[] { "site:SITE-A", "site:SITE-B" }, new[] { "SITE-A", "SITE-B" })]
    [InlineData("work-center", new[] { "site:SITE-001", "self:user-emp-049" }, new[] { "SITE-001" })]
    [InlineData("workshop", new[] { "site:SITE-B", "self:user-emp-049", "site:SITE-A", "work-center:WC-01" }, new[] { "SITE-A", "SITE-B" })]
    public async Task Work_center_and_workshop_directories_narrow_to_the_union_of_authorized_sites(
        string directoryType,
        string[] grants,
        string[] expectedSites)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            .. grants.Select(grant => grant.Split(':')).Select(parts =>
                Grant(parts[0], parts[1], BusinessGatewayPermissions.MasterDataResourcesRead)),
        ]);
        var (masterData, masterDataHandler) = MasterDataOverWire(directoryType);
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedSites, QueryValues(masterDataHandler, "siteCodes"));
        Assert.Empty(QueryValues(masterDataHandler, "siteCode"));
    }

    // 并集只收用户实际持有、且适用本权限的工厂：别的权限上的工厂授权不会被放进集合。
    [Theory]
    [InlineData("work-center")]
    [InlineData("workshop")]
    public async Task Work_center_and_workshop_union_excludes_sites_granted_for_another_permission(string directoryType)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.MasterDataResourcesRead),
            Grant("site", "SITE-B", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var (masterData, masterDataHandler) = MasterDataOverWire(directoryType);
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["SITE-A"], QueryValues(masterDataHandler, "siteCodes"));
    }

    [Theory]
    [InlineData("work-center")]
    [InlineData("workshop")]
    public async Task Organization_wide_grant_reads_work_center_and_workshop_without_site_narrowing(string directoryType)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.MasterDataResourcesRead, organizationWide: true),
            Grant("self", "user-admin", BusinessGatewayPermissions.MasterDataResourcesRead),
        ]);
        var (masterData, masterDataHandler) = MasterDataOverWire(directoryType);
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(masterDataHandler.RequestUri);
        Assert.Empty(QueryValues(masterDataHandler, "siteCodes"));
    }

    // 没有任何工厂授权（只有 self）、或显式请求一个没授权的工厂：拒绝，且不打到主数据。
    [Theory]
    [InlineData("work-center", "")]
    [InlineData("workshop", "")]
    [InlineData("work-center", "&scopeKind=site&scopeId=SITE-B")]
    [InlineData("workshop", "&scopeKind=site&scopeId=SITE-B")]
    public async Task Work_center_and_workshop_without_an_authorized_site_fail_closed(string directoryType, string explicitScope)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants: explicitScope.Length == 0
            ? [Grant("self", "user-emp-049", BusinessGatewayPermissions.MasterDataResourcesRead)]
            : [Grant("site", "SITE-A", BusinessGatewayPermissions.MasterDataResourcesRead)]);
        var (masterData, masterDataHandler) = MasterDataOverWire(directoryType);
        await using var lease = LeaseHost(auth, InventoryNotCalled(), masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev{explicitScope}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(masterDataHandler.RequestUri);
    }

    private static (IBusinessMasterDataClient Client, JsonHandler Handler) MasterDataOverWire(string directoryType)
    {
        var handler = new JsonHandler(
            $"{{\"resources\":[{{\"resourceType\":\"{directoryType}\",\"code\":\"R-001\",\"displayName\":\"资源 1\",\"active\":true,\"snapshotVersion\":\"v1\",\"siteCode\":\"SITE-A\"}}],\"total\":1}}");
        return (new HttpBusinessMasterDataClient(new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") }), handler);
    }

    private static JsonHandler InventoryNotCalled() => new("{}");

    private static string[] QueryValues(JsonHandler downstream, string name) =>
        downstream.RequestUri is null
            ? []
            :
            [
                .. downstream.RequestUri.Query.TrimStart('?').Split('&')
                    .Where(pair => pair.StartsWith($"{name}=", StringComparison.Ordinal))
                    .Select(pair => Uri.UnescapeDataString(pair[(name.Length + 1)..])),
            ];
}
