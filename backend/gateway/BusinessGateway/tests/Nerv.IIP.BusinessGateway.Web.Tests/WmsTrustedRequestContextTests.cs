using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class WmsTrustedRequestContextTests
{
    [Fact]
    public async Task Resolver_uses_active_organization_site_directory_for_worker_without_team()
    {
        const string permissionCode = "business.wms.counts.read";
        var masterData = new RecordingMasterDataClient
        {
            PrincipalWorkContext = EmptyPrincipalWorkContext(),
            Resources =
            [
                Site("SITE-001", active: true),
                Site("SITE-DISABLED", active: false),
                new BusinessConsoleResourceItem(
                    "workshop",
                    "WS-001",
                    "Workshop 001",
                    true,
                    "v1"),
            ],
        };
        var authorization = BusinessGatewayAuthorizationResult.Allowed(
            "user-emp-049",
            "user",
            "emp049",
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-pda-warehouse",
                    "site",
                    "SITE-001",
                    [permissionCode]),
            ]);
        var resolver = new WmsTrustedRequestContextResolver(
            masterData,
            new TestInternalServiceTokenProvider("internal-test-token"));

        var trusted = await resolver.ResolveAsync(
            authorization,
            "org-001",
            "env-dev",
            permissionCode,
            CancellationToken.None);

        Assert.Equal("user-emp-049", trusted.ActorPrincipalId);
        Assert.Equal(["SITE-001"], trusted.AuthorizedSiteCodes);
        Assert.Null(masterData.LastPrincipalWorkContextRequest);
        Assert.Equal(
            new BusinessConsoleListResourcesRequest(
                "org-001",
                "env-dev",
                "site",
                IncludeDisabled: false,
                Take: 500,
                All: true),
            masterData.LastListResourcesRequest);
    }

    [Fact]
    public async Task Resolver_expands_organization_grant_only_to_active_directory_sites()
    {
        const string permissionCode = "business.wms.receipts.read";
        var masterData = new RecordingMasterDataClient
        {
            PrincipalWorkContext = EmptyPrincipalWorkContext(),
            Resources =
            [
                Site("SITE-B", active: true),
                Site("SITE-A", active: true),
                Site("SITE-DISABLED", active: false),
            ],
        };
        var authorization = BusinessGatewayAuthorizationResult.Allowed(
            "user-emp-049",
            "user",
            "emp049",
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-pda-warehouse",
                    "organization",
                    "org-001",
                    [permissionCode],
                    OrganizationWide: true),
            ]);
        var resolver = new WmsTrustedRequestContextResolver(
            masterData,
            new TestInternalServiceTokenProvider("internal-test-token"));

        var trusted = await resolver.ResolveAsync(
            authorization,
            "org-001",
            "env-dev",
            permissionCode,
            CancellationToken.None);

        Assert.Equal(["SITE-A", "SITE-B"], trusted.AuthorizedSiteCodes);
    }

    [Fact]
    public async Task Resolver_rejects_exact_site_missing_from_requested_organization_directory()
    {
        const string permissionCode = "business.wms.receipts.read";
        var masterData = new RecordingMasterDataClient
        {
            PrincipalWorkContext = EmptyPrincipalWorkContext(),
            Resources = [Site("SITE-OTHER-ORG", active: true)],
        };
        var authorization = BusinessGatewayAuthorizationResult.Allowed(
            "user-emp-049",
            "user",
            "emp049",
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-pda-warehouse",
                    "site",
                    "SITE-001",
                    [permissionCode]),
            ]);
        var resolver = new WmsTrustedRequestContextResolver(
            masterData,
            new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(
            () => resolver.ResolveAsync(
                authorization,
                "org-002",
                "env-dev",
                permissionCode,
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public void From_resolved_sites_normalizes_only_the_authorized_candidate_set()
    {
        var authorization = BusinessGatewayAuthorizationResult.Allowed(
            " user-emp-049 ",
            "user",
            "emp049");

        var trusted = WmsTrustedRequestContext.FromResolvedSites(
            authorization,
            [" SITE-B ", "SITE-A", "SITE-A", " "]);

        Assert.Equal("user-emp-049", trusted.ActorPrincipalId);
        Assert.Equal(["SITE-A", "SITE-B"], trusted.AuthorizedSiteCodes);
    }

    [Fact]
    public void Resolve_scope_defaults_to_self_and_allows_only_supported_explicit_scopes()
    {
        var trusted = new WmsTrustedRequestContext(
            "user-emp-049",
            ["SITE-A"]);

        Assert.Equal(
            new WmsTrustedScopeSelection("self", "user-emp-049"),
            trusted.ResolveScope(null, null));
        Assert.Equal(
            new WmsTrustedScopeSelection("self", "user-emp-049"),
            trusted.ResolveScope(" SELF ", " user-emp-049 "));
        Assert.Equal(
            new WmsTrustedScopeSelection("work-pool", "POOL-A"),
            trusted.ResolveScope(" WORK-POOL ", " POOL-A "));
        Assert.Equal(
            new WmsTrustedScopeSelection("site", "SITE-A"),
            trusted.ResolveScope(" SITE ", " SITE-A "));
    }

    [Theory]
    [InlineData(null, "user-emp-049")]
    [InlineData("self", null)]
    [InlineData("self", "other-user")]
    [InlineData("site", "SITE-B")]
    [InlineData("organization", "org-001")]
    [InlineData("team", "TEAM-A")]
    public void Resolve_scope_rejects_partial_forged_or_unsupported_scope(
        string? scopeKind,
        string? scopeId)
    {
        var trusted = new WmsTrustedRequestContext(
            "user-emp-049",
            ["SITE-A"]);

        var exception = Assert.Throws<BusinessServiceProxyException>(
            () => trusted.ResolveScope(scopeKind, scopeId));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public void From_resolved_sites_fails_closed_without_allowed_principal_and_site_candidate()
    {
        var candidates = new BusinessGatewayAuthorizationResult?[]
        {
            null,
            BusinessGatewayAuthorizationResult.Forbidden("forbidden"),
            BusinessGatewayAuthorizationResult.Allowed(
                "",
                "user",
                "emp049"),
            BusinessGatewayAuthorizationResult.Allowed(
                "user-emp-049",
                "user",
                "emp049",
                new AuthorizationDataScope([], [], [], DenyAll: true)),
            BusinessGatewayAuthorizationResult.Allowed(
                "user-emp-049",
                "user",
                "emp049"),
        };

        foreach (var authorization in candidates)
        {
            var exception = Assert.Throws<BusinessServiceProxyException>(
                () => WmsTrustedRequestContext.FromResolvedSites(
                    authorization,
                    []));

            Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        }
    }

    private static BusinessConsoleResourceItem Site(string code, bool active) =>
        new("site", code, code, active, "v1");

    private static BusinessMasterDataPrincipalWorkContextResponse EmptyPrincipalWorkContext() =>
        new(
            "resolved",
            new BusinessMasterDataWorkContextWorker(
                "worker-049",
                "user-emp-049",
                "EMP-049",
                "吴桂芳",
                null,
                null,
                "库管",
                "active"),
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);
}
