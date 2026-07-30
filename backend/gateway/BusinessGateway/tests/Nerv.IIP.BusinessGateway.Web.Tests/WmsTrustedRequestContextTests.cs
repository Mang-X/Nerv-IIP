using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class WmsTrustedRequestContextTests
{
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
}
