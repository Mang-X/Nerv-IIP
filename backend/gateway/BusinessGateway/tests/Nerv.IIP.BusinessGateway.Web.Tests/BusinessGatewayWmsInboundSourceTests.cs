using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed partial class BusinessGatewayWmsTests
{
    // PublicContract: #4255 / #3825 r1. The receipt-only opt-in uses this permission's server grants.
    [Theory]
    [InlineData("", "self", "user-admin")]
    [InlineData("&scopeKind=self&scopeId=user-admin", "self", "user-admin")]
    [InlineData("&scopeKind=site&scopeId=SITE-A", "site", "SITE-A")]
    [InlineData("&scopeKind=work-pool&scopeId=POOL-A", "work-pool", "POOL-A")]
    [InlineData("&scopeKind=authorized-sites&scopeId=all&authorizedSiteCodes=SITE-C", "authorized-sites", "all")]
    public async Task Receipt_source_mode_uses_permission_sites_and_preserves_legacy_scope(
        string query, string expectedKind, string expectedId)
    {
        var wms = new RecordingWmsClient();
        var permission = BusinessGatewayPermissions.WmsReceiptsRead;
        var auth = ScopeAuth([permission],
            new AuthorizationScopeGrant("role", "warehouse", "site", "SITE-A", [permission]),
            new AuthorizationScopeGrant("role", "warehouse", "site", "SITE-B", [permission]),
            new AuthorizationScopeGrant("role", "other", "site", "SITE-001", [BusinessGatewayPermissions.WmsShipmentsRead]));
        await using var lease = LeaseHost(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(
            "/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&keyword=IN-2026&skip=2&take=2" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["SITE-A", "SITE-B"], wms.LastInboundRequest!.AuthorizedSiteCodes);
        Assert.Equal(expectedKind, wms.LastInboundRequest.ScopeKind);
        Assert.Equal(expectedId, wms.LastInboundRequest.ScopeId);
        Assert.Equal("user-admin", wms.LastInboundRequest.ActorPrincipalId);
        Assert.Equal("IN-2026", wms.LastInboundRequest.Keyword);
        Assert.Equal(2, wms.LastInboundRequest.Skip);
        Assert.Equal(2, wms.LastInboundRequest.Take);
    }

    [Theory]
    [InlineData("inbound-orders", "authorized-sites", "SITE-A", HttpStatusCode.Forbidden)]
    [InlineData("outbound-orders", "authorized-sites", "all", HttpStatusCode.BadRequest)]
    [InlineData("putaway-tasks", "authorized-sites", "all", HttpStatusCode.BadRequest)]
    [InlineData("inbound-orders", "site", "SITE-C", HttpStatusCode.Forbidden)]
    public async Task Receipt_source_mode_does_not_expand_other_lists_or_accept_invalid_scope(
        string resource, string kind, string id, HttpStatusCode expected)
    {
        await using var lease = LeaseHost(OrganizationScopeAuth(
            BusinessGatewayPermissions.WmsReceiptsRead, BusinessGatewayPermissions.WmsShipmentsRead));
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(
            $"/api/business-console/v1/wms/{resource}?organizationId=org-001&environmentId=env-dev&scopeKind={kind}&scopeId={id}");
        Assert.Equal(expected, response.StatusCode);
    }
}
