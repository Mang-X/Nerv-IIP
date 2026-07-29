using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class WmsTrustedRequestContextTests
{
    private const string Permission = "business.wms.receipts.read";

    [Fact]
    public void From_authorization_accepts_only_role_or_membership_exact_site_grants_for_permission()
    {
        var authorization = BusinessGatewayAuthorizationResult.Allowed(
            " user-emp-049 ",
            "user",
            "emp049",
            scopeGrants:
            [
                new("role", "warehouse-role", "site", " SITE-B ", [Permission]),
                new("membership", "warehouse-membership", "site", "SITE-A", [Permission]),
                new("membership", "duplicate-membership", "site", "SITE-A", [Permission]),
                new("user", "direct-user-grant", "site", "FORGED-SITE", [Permission]),
                new("role", "wrong-permission", "site", "OTHER-SITE", ["business.wms.shipments.read"]),
                new("role", "wrong-scope-kind", "work-pool", "POOL-A", [Permission]),
            ]);

        var trusted = WmsTrustedRequestContext.FromAuthorization(
            authorization,
            Permission);

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
    public void From_authorization_fails_closed_without_allowed_principal_and_exact_site()
    {
        var candidates = new BusinessGatewayAuthorizationResult?[]
        {
            null,
            BusinessGatewayAuthorizationResult.Forbidden("forbidden"),
            BusinessGatewayAuthorizationResult.Allowed(
                "",
                "user",
                "emp049",
                scopeGrants:
                [
                    new("role", "warehouse-role", "site", "SITE-A", [Permission]),
                ]),
            BusinessGatewayAuthorizationResult.Allowed(
                "user-emp-049",
                "user",
                "emp049",
                new AuthorizationDataScope([], [], [], DenyAll: true),
                [
                    new("role", "warehouse-role", "site", "SITE-A", [Permission]),
                ]),
            BusinessGatewayAuthorizationResult.Allowed(
                "user-emp-049",
                "user",
                "emp049",
                scopeGrants:
                [
                    new("role", "self-role", "self", "user-emp-049", [Permission]),
                ]),
        };

        foreach (var authorization in candidates)
        {
            var exception = Assert.Throws<BusinessServiceProxyException>(
                () => WmsTrustedRequestContext.FromAuthorization(
                    authorization,
                    Permission));

            Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        }
    }
}
