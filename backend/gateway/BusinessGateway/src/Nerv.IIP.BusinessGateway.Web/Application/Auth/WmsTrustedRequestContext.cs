using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed record WmsTrustedScopeSelection(string ScopeKind, string ScopeId);

public sealed record WmsTrustedRequestContext(
    string ActorPrincipalId,
    IReadOnlyList<string> AuthorizedSiteCodes)
{
    public WmsTrustedScopeSelection ResolveScope(
        string? requestedScopeKind,
        string? requestedScopeId)
    {
        var hasKind = !string.IsNullOrWhiteSpace(requestedScopeKind);
        var hasId = !string.IsNullOrWhiteSpace(requestedScopeId);
        if (hasKind != hasId)
        {
            throw Forbidden();
        }

        if (!hasKind)
        {
            return new WmsTrustedScopeSelection("self", ActorPrincipalId);
        }

        var scopeKind = requestedScopeKind!.Trim().ToLowerInvariant();
        var scopeId = requestedScopeId!.Trim();
        var authorized = scopeKind switch
        {
            "self" => string.Equals(scopeId, ActorPrincipalId, StringComparison.Ordinal),
            "work-pool" => true,
            "site" => AuthorizedSiteCodes.Contains(scopeId, StringComparer.Ordinal),
            _ => false,
        };
        return authorized
            ? new WmsTrustedScopeSelection(scopeKind, scopeId)
            : throw Forbidden();
    }

    public static WmsTrustedRequestContext FromResolvedSites(
        BusinessGatewayAuthorizationResult? authorization,
        IReadOnlyCollection<string> authorizedSiteCodes)
    {
        if (authorization is null
            || !authorization.IsAllowed
            || authorization.DataScope?.DenyAll == true
            || string.IsNullOrWhiteSpace(authorization.PrincipalId))
        {
            throw Forbidden();
        }

        var normalizedSiteCodes = authorizedSiteCodes
            .Where(siteCode => !string.IsNullOrWhiteSpace(siteCode))
            .Select(siteCode => siteCode.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedSiteCodes.Length == 0)
        {
            throw Forbidden();
        }

        return new WmsTrustedRequestContext(
            authorization.PrincipalId.Trim(),
            normalizedSiteCodes);
    }

    private static BusinessServiceProxyException Forbidden() =>
        new(HttpStatusCode.Forbidden, "work-scope-not-authorized");
}

public sealed class WmsTrustedRequestContextResolver(
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
{
    public async Task<WmsTrustedRequestContext> ResolveAsync(
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        if (authorization is null
            || !authorization.IsAllowed
            || authorization.DataScope?.DenyAll == true
            || string.IsNullOrWhiteSpace(authorization.PrincipalId))
        {
            return WmsTrustedRequestContext.FromResolvedSites(authorization, []);
        }

        var siteDirectory = await masterData.ListResourcesAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleListResourcesRequest(
                organizationId,
                environmentId,
                "site",
                IncludeDisabled: false,
                Take: 500,
                All: true),
            cancellationToken);
        var authorizedSiteCodes = PrincipalWorkContextAuthorizationResolver.ResolveSiteCandidates(
                siteDirectory.Resources,
                authorization,
                organizationId,
                permissionCode)
            .Select(scope => scope.Id)
            .ToArray();

        return WmsTrustedRequestContext.FromResolvedSites(
            authorization,
            authorizedSiteCodes);
    }
}
