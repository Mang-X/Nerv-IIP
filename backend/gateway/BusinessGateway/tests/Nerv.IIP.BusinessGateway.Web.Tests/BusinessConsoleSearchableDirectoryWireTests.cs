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
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleSearchableDirectoryWireTests
{
    [Fact]
    public async Task Directory_endpoint_authorizes_only_owner_permission_and_forwards_site_scope()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var downstream = new JsonHandler("{\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\",\"reasonCode\":null}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&scopeKind=site&scopeId=SITE-A");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.InventoryLedgerRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("site", auth.LastRequirement.ResourceType);
        Assert.Equal("SITE-A", auth.LastRequirement.ResourceId);
        Assert.Equal(["SITE-A"], AuthorizedSites(downstream));
    }

    [Fact]
    public async Task Restricted_scope_grant_cannot_be_replaced()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&scopeKind=site&scopeId=SITE-B");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Single_compatible_restricted_grant_is_derived_when_scope_is_omitted()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["SITE-A"], AuthorizedSites(downstream));
    }

    // #3832 审核 B1：库位 / 批次 / 序列号按工厂切分，可见范围是授权工厂的并集。
    // 种子账号 user-emp-049「仓储库管（PDA）」= 角色 site:SITE-001 + membership self；
    // 过去任一条 self 或第二个工厂都会让整个目录 403，WMS 表单里的库位选不到、单子提交不了。
    [Theory]
    [InlineData("location", new[] { "site:SITE-A", "site:SITE-B" }, new[] { "SITE-A", "SITE-B" })]
    [InlineData("location", new[] { "site:SITE-001", "self:user-emp-049" }, new[] { "SITE-001" })]
    [InlineData("batch", new[] { "site:SITE-B", "self:user-emp-049", "site:SITE-A" }, new[] { "SITE-A", "SITE-B" })]
    [InlineData("serial", new[] { "site:SITE-A", "work-center:WC-01" }, new[] { "SITE-A" })]
    public async Task Inventory_directory_narrows_to_the_union_of_authorized_sites(
        string directoryType,
        string[] grants,
        string[] expectedSites)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            .. grants.Select(grant => grant.Split(':')).Select(parts =>
                Grant(parts[0], parts[1], BusinessGatewayPermissions.InventoryLedgerRead)),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedSites, AuthorizedSites(downstream));
        Assert.DoesNotContain("siteCode=", downstream.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Organization_wide_grant_reads_inventory_directory_without_site_narrowing()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.InventoryLedgerRead, organizationWide: true),
            Grant("self", "user-admin", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(AuthorizedSites(downstream));
    }

    [Fact]
    public async Task Grant_without_any_site_fails_closed()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("self", "user-admin", BusinessGatewayPermissions.InventoryLedgerRead),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Malformed_scope_grant_fails_closed_without_reaching_owner()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            new AuthorizationScopeGrant(
                null!,
                "role-directory-reader",
                "site",
                "SITE-A",
                [BusinessGatewayPermissions.InventoryLedgerRead]),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    // 不适用本权限的工厂授权不参与并集：它既不拒绝，也不把别的工厂放进来。
    [Fact]
    public async Task Site_grant_for_another_permission_is_not_part_of_the_union()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.InventoryLedgerRead),
            Grant("site", "SITE-B", BusinessGatewayPermissions.MasterDataResourcesRead),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["SITE-A"], AuthorizedSites(downstream));
    }

    [Theory]
    [InlineData("malformed-source")]
    [InlineData("restricted-organization")]
    public async Task Valid_grant_mixed_with_unrepresentable_grant_fails_closed_without_reaching_owner(
        string extraGrantKind)
    {
        var extraGrant = extraGrantKind switch
        {
            "malformed-source" => new AuthorizationScopeGrant(
                null!,
                "role-directory-reader",
                "site",
                "SITE-B",
                [BusinessGatewayPermissions.InventoryLedgerRead]),
            "restricted-organization" => Grant(
                "organization",
                "org-001",
                BusinessGatewayPermissions.InventoryLedgerRead),
            _ => throw new ArgumentOutOfRangeException(nameof(extraGrantKind)),
        };
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("site", "SITE-A", BusinessGatewayPermissions.InventoryLedgerRead),
            extraGrant,
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    // #3125：无范围维度的目录（SupportedScopeKinds 为空集）上，只持受限范围授权的主体也必须读到词表。
    // 受影响的是同一类目录，不是单条登记：material / priority / defect-code / scrap-reason /
    // downtime-reason / maintenance-reason。这里在 maintenance 与 quality 两个权威源上各取一条，
    // 证明放行是「类的行为」而不是对 downtime-reason 的单例修补。
    [Theory]
    [InlineData("downtime-reason", "self", "user-emp-010")]
    [InlineData("downtime-reason", "site", "SITE-001")]
    [InlineData("defect-code", "self", "user-emp-010")]
    public async Task Restricted_grant_reads_directories_without_a_scope_dimension(
        string directoryType,
        string scopeKind,
        string scopeId)
    {
        var permissionCode = directoryType == "downtime-reason"
            ? BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead
            : BusinessGatewayPermissions.QualityInspectionRecordsRead;
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            new AuthorizationScopeGrant("membership", "user-emp-010:org-001:env-dev", scopeKind, scopeId, [permissionCode]),
        ]);
        var targetHandler = new JsonHandler(directoryType == "downtime-reason"
            ? "{\"items\":[{\"downtimeReasonId\":{\"id\":\"01900000-0000-7000-8000-000000000001\"},\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"reasonCode\":\"BREAKDOWN\",\"description\":\"Breakdown\",\"reasonCategory\":\"unplanned\",\"lossCategory\":\"availability\"}],\"skip\":0,\"take\":20,\"total\":1}"
            : "{\"items\":[{\"reasonCode\":\"SCRATCH\",\"reasonName\":\"Scratch\",\"groupName\":\"appearance\",\"severity\":\"minor\",\"defaultDisposition\":null,\"enabled\":true,\"snapshotVersion\":\"v1\"}],\"total\":1}");
        await using var lease = LeaseHost(
            auth,
            new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}"),
            quality: directoryType == "defect-code"
                ? new HttpBusinessQualityClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://quality.local") })
                : null,
            maintenance: directoryType == "downtime-reason"
                ? new HttpBusinessMaintenanceClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://maintenance.local") })
                : null);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(permissionCode, auth.LastRequirement!.PermissionCode);
        // 无范围维度的目录不会把 grant 的范围伪装成过滤条件转发给权威源。
        Assert.NotNull(targetHandler.RequestUri);
        Assert.DoesNotContain(scopeId, targetHandler.RequestUri!.Query, StringComparison.Ordinal);
    }

    // 放宽只覆盖「范围维度」这一条；租户边界仍然承重：范围落在别的组织上的 grant 依旧不可表示。
    [Fact]
    public async Task Cross_organization_grant_still_fails_closed_on_directories_without_a_scope_dimension()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-999", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead),
        ]);
        var maintenanceHandler = new JsonHandler("{\"items\":[],\"skip\":0,\"take\":20,\"total\":0}");
        await using var lease = LeaseHost(
            auth,
            new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}"),
            maintenance: new HttpBusinessMaintenanceClient(
                new HttpClient(maintenanceHandler) { BaseAddress = new Uri("http://maintenance.local") }));
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/downtime-reason?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Null(maintenanceHandler.RequestUri);
    }

    // 无范围维度不等于「接受并忽略范围」：显式范围仍在授权前 400，不会静默返回全量。
    [Fact]
    public async Task Explicit_scope_on_a_directory_without_a_scope_dimension_still_fails_before_authorization()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            new AuthorizationScopeGrant(
                "membership",
                "user-emp-010:org-001:env-dev",
                "self",
                "user-emp-010",
                [BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead]),
        ]);
        var maintenanceHandler = new JsonHandler("{\"items\":[],\"skip\":0,\"take\":20,\"total\":0}");
        await using var lease = LeaseHost(
            auth,
            new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}"),
            maintenance: new HttpBusinessMaintenanceClient(
                new HttpClient(maintenanceHandler) { BaseAddress = new Uri("http://maintenance.local") }));
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/downtime-reason?organizationId=org-001&environmentId=env-dev&scopeKind=self&scopeId=user-emp-010");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "directory-scope-unsupported",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(0, auth.CallCount);
        Assert.Null(maintenanceHandler.RequestUri);
    }

    [Fact]
    public async Task Explicit_organization_grant_is_required_when_scope_is_omitted()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.InventoryLedgerRead, organizationWide: true),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("organization", auth.LastRequirement!.ResourceType);
        Assert.Equal("org-001", auth.LastRequirement.ResourceId);
        Assert.NotNull(downstream.RequestUri);
    }

    [Fact]
    public async Task Cross_owner_permission_and_invalid_scope_fail_before_downstream()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MasterDataResourcesRead);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

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
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

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
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Page_offset_overflow_is_rejected_without_authorization_or_downstream_call()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.InventoryLedgerRead, organizationWide: true),
        ]);
        var downstream = new JsonHandler("{\"items\":[],\"total\":0,\"skip\":0,\"take\":100,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&pageIndex=2147483647&pageSize=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Null(downstream.RequestUri);
    }

    [Fact]
    public async Task Largest_representable_page_offset_is_forwarded_without_wrapping()
    {
        const int expectedSkip = 2_147_483_600;
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.InventoryLedgerRead, organizationWide: true),
        ]);
        var downstream = new JsonHandler($"{{\"items\":[],\"total\":0,\"skip\":{expectedSkip},\"take\":100,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}}");
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&pageIndex=21474837&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"skip={expectedSkip}", downstream.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Largest_representable_page_rejects_owner_total_that_cannot_cover_the_page()
    {
        const int expectedSkip = 2_147_483_600;
        var items = Enumerable.Range(1, 100)
            .Select(index => new
            {
                id = $"location-{index}",
                code = $"LOC-{index}",
                display = $"Location {index}",
                directoryType = "location",
                siteCode = "SITE-A",
                locationCode = $"LOC-{index}",
                skuCode = (string?)null,
                parentCode = (string?)null,
                snapshotVersion = "v1",
            })
            .ToArray();
        var downstreamPayload = JsonSerializer.Serialize(new
        {
            items,
            total = int.MaxValue,
            skip = expectedSkip,
            take = 100,
            status = "available",
            sourceKind = "inventory.stock-locations",
            asOfUtc = "2026-08-01T00:00:00Z",
            reasonCode = (string?)null,
        });
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.InventoryLedgerRead, organizationWide: true),
        ]);
        var downstream = new JsonHandler(downstreamPayload);
        await using var lease = LeaseHost(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/location?organizationId=org-001&environmentId=env-dev&pageIndex=21474837&pageSize=100");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Station_route_uses_station_master_data_code_as_id_and_code()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("work-center", "WC-001", BusinessGatewayPermissions.MasterDataResourcesRead),
        ]);
        var downstream = new JsonHandler("{\"status\":\"available\",\"reasonCode\":null,\"items\":[],\"total\":0}");
        var masterData = new RecordingMasterDataClient
        {
            Resources =
            [
                new BusinessConsoleResourceItem(
                    "station",
                    "ST-001",
                    "装配工位 1",
                    true,
                    "v1",
                    SiteCode: "SITE-001",
                    LineCode: "LINE-001",
                    WorkshopCode: "WS-001",
                    WorkCenterCode: "WC-001",
                    StationCode: "ST-001"),
            ],
        };
        await using var lease = LeaseHost(auth, downstream, masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/station?organizationId=org-001&environmentId=env-dev&scopeKind=work-center&scopeId=WC-001&pageIndex=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = body.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("ST-001", item.GetProperty("id").GetString());
        Assert.Equal("ST-001", item.GetProperty("code").GetString());
        Assert.Equal("装配工位 1", item.GetProperty("displayName").GetString());
        Assert.Equal("WC-001", item.GetProperty("context").GetProperty("workCenterCode").GetString());
        Assert.Equal("station", masterData.LastListResourcesRequest!.ResourceType);
        Assert.Equal("WC-001", masterData.LastListResourcesRequest.WorkCenterCode);
    }

    [Theory]
    [InlineData("{\"items\":[{\"id\":\"inventory-directory:batch:5:SKU-1:5:LOT-1\",\"code\":\"LOT-1\",\"display\":\"LOT-1 · SKU-1\",\"directoryType\":\"batch\",\"siteCode\":\"SITE-A\",\"locationCode\":null,\"skuCode\":\"SKU-1\",\"parentCode\":null,\"snapshotVersion\":\"v1\"}],\"total\":1,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-ledgers\",\"asOfUtc\":\"2026-08-01T00:00:00Z\",\"reasonCode\":null}")]
    [InlineData("{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{\"items\":[{\"id\":\"inventory-directory:batch:5:SKU-1:5:LOT-1\",\"code\":\"LOT-1\",\"display\":\"LOT-1 · SKU-1\",\"directoryType\":\"batch\",\"siteCode\":\"SITE-A\",\"locationCode\":null,\"skuCode\":\"SKU-1\",\"parentCode\":null,\"snapshotVersion\":\"v1\"}],\"total\":1,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-ledgers\",\"asOfUtc\":\"2026-08-01T00:00:00Z\",\"reasonCode\":null}}")]
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

        var item = Assert.Single(response.Items);
        Assert.Equal("inventory-directory:batch:5:SKU-1:5:LOT-1", item.Id);
        Assert.Equal("LOT-1 · SKU-1", item.Display);
        var serialized = JsonSerializer.Serialize(response);
        Assert.Contains("\"directoryType\":\"batch\"", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourceKind\":\"inventory.stock-ledgers\"", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"skip\":0", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"take\":20", serialized, StringComparison.OrdinalIgnoreCase);
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

    public static TheoryData<string, string, string> MalformedOwnerPayloads => new()
    {
        { "location", BusinessGatewayPermissions.InventoryLedgerRead, "{\"items\":null,\"total\":0,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}" },
        { "location", BusinessGatewayPermissions.InventoryLedgerRead, "{\"items\":[{\"id\":\"\",\"code\":\"LOC-A\",\"display\":\"\",\"directoryType\":\"location\",\"siteCode\":\"SITE-A\",\"locationCode\":\"LOC-A\",\"skuCode\":null,\"parentCode\":null,\"snapshotVersion\":\"v1\"}],\"total\":0,\"skip\":1,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}" },
        { "material", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"resources\":null,\"total\":0}" },
        { "material", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"resources\":[{\"resourceType\":\"sku\",\"code\":\"\",\"displayName\":\"\",\"active\":true,\"snapshotVersion\":\"v1\"}],\"total\":0}" },
        { "personnel", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"items\":null,\"totalCount\":0,\"pageIndex\":1,\"pageSize\":20}" },
        { "personnel", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"items\":[{\"userId\":\"\",\"employeeNo\":\"\",\"name\":\"\",\"departmentCode\":null,\"departmentName\":null,\"jobTitle\":null,\"employmentStatus\":\"active\",\"phone\":null,\"active\":true,\"teams\":[],\"skills\":[],\"snapshotVersion\":\"v1\"}],\"totalCount\":0,\"pageIndex\":2,\"pageSize\":20}" },
        { "defect-code", BusinessGatewayPermissions.QualityInspectionRecordsRead, "{\"items\":null,\"total\":0}" },
        { "defect-code", BusinessGatewayPermissions.QualityInspectionRecordsRead, "{\"items\":[{\"reasonCode\":\"\",\"reasonName\":\"\",\"groupName\":\"group\",\"severity\":\"minor\",\"defaultDisposition\":null,\"enabled\":true,\"snapshotVersion\":\"v1\"}],\"total\":0}" },
        { "downtime-reason", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead, "{\"items\":null,\"skip\":0,\"take\":20,\"total\":0}" },
        { "downtime-reason", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead, "{\"items\":[{\"downtimeReasonId\":{\"id\":\"01900000-0000-7000-8000-000000000001\"},\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"reasonCode\":\"\",\"description\":\"\",\"reasonCategory\":\"\",\"lossCategory\":\"\"}],\"skip\":1,\"take\":20,\"total\":0}" },
    };

    public static TheoryData<string, string, string> AuthoritativeFailureEnvelopeCases => new()
    {
        { "material", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":null}" },
        { "material", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":{\"resources\":[],\"total\":0}}" },
        { "personnel", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":null}" },
        { "personnel", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":20}}" },
        { "priority", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":null}" },
        { "priority", BusinessGatewayPermissions.MasterDataResourcesRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":{\"resources\":[],\"total\":0}}" },
        { "defect-code", BusinessGatewayPermissions.QualityInspectionRecordsRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":null}" },
        { "defect-code", BusinessGatewayPermissions.QualityInspectionRecordsRead, "{\"success\":false,\"message\":\"not available\",\"code\":409,\"data\":{\"items\":[],\"total\":0}}" },
    };

    [Theory]
    [MemberData(nameof(AuthoritativeFailureEnvelopeCases))]
    public async Task Authoritative_failure_envelopes_return_safe_502_for_each_owner_and_priority_probe(
        string directoryType,
        string permissionCode,
        string failurePayload)
    {
        var targetHandler = directoryType == "priority"
            ? new SequenceJsonHandler("{\"resources\":[],\"total\":0}", failurePayload)
            : new SequenceJsonHandler(failurePayload);
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", permissionCode, organizationWide: true),
        ]);
        var inventoryHandler = new JsonHandler("{\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        var masterData = directoryType is "material" or "personnel" or "priority"
            ? new HttpBusinessMasterDataClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://master-data.local") })
            : null;
        var quality = directoryType == "defect-code"
            ? new HttpBusinessQualityClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://quality.local") })
            : null;
        await using var lease = LeaseHost(
            auth,
            inventoryHandler,
            masterData,
            quality);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(directoryType == "priority" ? 2 : 1, targetHandler.RequestCount);
    }

    [Theory]
    [InlineData("{\"resources\":[{\"resourceType\":\"reference-data\",\"code\":\"\",\"displayName\":\"\",\"active\":true,\"snapshotVersion\":\"v1\"}],\"total\":1}")]
    [InlineData("{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{\"resources\":[{\"resourceType\":\"reference-data\",\"code\":\"\",\"displayName\":\"\",\"active\":true,\"snapshotVersion\":\"v1\"}],\"total\":1}}")]
    public async Task Priority_authority_probe_rejects_malformed_resource_semantics_for_raw_and_envelope(
        string malformedProbePayload)
    {
        var targetHandler = new SequenceJsonHandler(
            "{\"resources\":[],\"total\":0}",
            malformedProbePayload);
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant(
                "organization",
                "org-001",
                BusinessGatewayPermissions.MasterDataResourcesRead,
                organizationWide: true),
        ]);
        var masterData = new HttpBusinessMasterDataClient(
            new HttpClient(targetHandler) { BaseAddress = new Uri("http://master-data.local") });
        var inventoryHandler = new JsonHandler("{\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, inventoryHandler, masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/priority?organizationId=org-001&environmentId=env-dev&keyword=urgent");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, targetHandler.RequestCount);
    }

    [Theory]
    [InlineData("{\"resources\":[],\"total\":1}")]
    [InlineData("{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{\"resources\":[],\"total\":1}}")]
    public async Task Priority_authority_probe_requires_an_item_when_total_is_positive_for_raw_and_envelope(
        string malformedProbePayload)
    {
        var targetHandler = new SequenceJsonHandler(
            "{\"resources\":[],\"total\":0}",
            malformedProbePayload);
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant(
                "organization",
                "org-001",
                BusinessGatewayPermissions.MasterDataResourcesRead,
                organizationWide: true),
        ]);
        var masterData = new HttpBusinessMasterDataClient(
            new HttpClient(targetHandler) { BaseAddress = new Uri("http://master-data.local") });
        var inventoryHandler = new JsonHandler("{\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
        await using var lease = LeaseHost(auth, inventoryHandler, masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/priority?organizationId=org-001&environmentId=env-dev&keyword=urgent");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, targetHandler.RequestCount);
    }

    [Theory]
    [MemberData(nameof(MalformedOwnerPayloads))]
    public async Task Malformed_authoritative_owner_semantics_return_502_for_raw_and_envelope(
        string directoryType,
        string permissionCode,
        string payload)
    {
        foreach (var wirePayload in new[]
                 {
                     payload,
                     $"{{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{payload}}}",
                 })
        {
            var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
            [
                Grant("organization", "org-001", permissionCode, organizationWide: true),
            ]);
            var targetHandler = new JsonHandler(wirePayload);
            var inventoryHandler = directoryType == "location"
                ? targetHandler
                : new JsonHandler("{\"items\":[],\"total\":0,\"skip\":0,\"take\":20,\"status\":\"available\",\"sourceKind\":\"inventory.stock-locations\",\"asOfUtc\":\"2026-08-01T00:00:00Z\"}");
            var masterData = directoryType is "material" or "personnel"
                ? new HttpBusinessMasterDataClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://master-data.local") })
                : null;
            var quality = directoryType == "defect-code"
                ? new HttpBusinessQualityClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://quality.local") })
                : null;
            var maintenance = directoryType == "downtime-reason"
                ? new HttpBusinessMaintenanceClient(new HttpClient(targetHandler) { BaseAddress = new Uri("http://maintenance.local") })
                : null;
            await using var lease = LeaseHost(
                auth,
                inventoryHandler,
                masterData,
                quality,
                maintenance);
            var client = lease.CreateClient();
            BusinessGatewayTestHost.Authenticated(client);

            var response = await client.GetAsync(
                $"/api/business-console/v1/directories/{directoryType}?organizationId=org-001&environmentId=env-dev");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
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

    private sealed class SequenceJsonHandler(params string[] payloads) : HttpMessageHandler
    {
        private int index;

        public int RequestCount => index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payloadIndex = Interlocked.Increment(ref index) - 1;
            var payload = payloads[Math.Min(payloadIndex, payloads.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static BusinessGatewayTestHostLease LeaseHost(
        IBusinessGatewayAuthorizationClient auth,
        JsonHandler inventoryHandler,
        IBusinessMasterDataClient? masterData = null,
        IBusinessQualityClient? quality = null,
        IBusinessMaintenanceClient? maintenance = null) =>
        BusinessGatewayTestHost.Lease(auth, services =>
            {
                services.RemoveAll<IBusinessInventoryClient>();
                services.AddSingleton<IBusinessInventoryClient>(new HttpBusinessInventoryClient(
                    new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory.local") },
                    Options.Create(new BusinessGatewayInventoryForwardedPermissionOptions())));
                if (masterData is not null)
                {
                    services.RemoveAll<IBusinessMasterDataClient>();
                    services.AddSingleton(masterData);
                }
                if (quality is not null)
                {
                    services.RemoveAll<IBusinessQualityClient>();
                    services.AddSingleton(quality);
                }
                if (maintenance is not null)
                {
                    services.RemoveAll<IBusinessMaintenanceClient>();
                    services.AddSingleton(maintenance);
                }

                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-token"));
            });

    private static string[] AuthorizedSites(JsonHandler downstream) =>
    [
        .. downstream.RequestUri!.Query.TrimStart('?').Split('&')
            .Where(pair => pair.StartsWith("authorizedSiteCodes=", StringComparison.Ordinal))
            .Select(pair => Uri.UnescapeDataString(pair["authorizedSiteCodes=".Length..])),
    ];

    private static AuthorizationScopeGrant Grant(
        string scopeKind,
        string scopeId,
        string permissionCode,
        bool organizationWide = false) =>
        new("role", "role-directory-reader", scopeKind, scopeId, [permissionCode], organizationWide);
}
