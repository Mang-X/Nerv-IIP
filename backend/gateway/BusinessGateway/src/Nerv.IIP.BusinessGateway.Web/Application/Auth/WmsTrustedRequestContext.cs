using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

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

    public static WmsTrustedRequestContext FromAuthorization(
        BusinessGatewayAuthorizationResult? authorization,
        string permissionCode)
    {
        if (authorization is null
            || !authorization.IsAllowed
            || authorization.DataScope?.DenyAll == true
            || string.IsNullOrWhiteSpace(authorization.PrincipalId))
        {
            throw Forbidden();
        }

        var authorizedSiteCodes = (authorization.ScopeGrants ?? [])
            .Where(grant =>
                grant is not null
                && IsTrustedGrantSource(grant.SourceKind)
                && string.Equals(grant.ScopeKind, "site", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(grant.ScopeId)
                && grant.ApplicablePermissionCodes?.Contains(permissionCode, StringComparer.Ordinal) == true)
            .Select(grant => grant.ScopeId.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (authorizedSiteCodes.Length == 0)
        {
            throw Forbidden();
        }

        return new WmsTrustedRequestContext(
            authorization.PrincipalId.Trim(),
            authorizedSiteCodes);
    }

    private static bool IsTrustedGrantSource(string? sourceKind) =>
        sourceKind?.Trim().ToLowerInvariant() is "role" or "membership";

    private static BusinessServiceProxyException Forbidden() =>
        new(HttpStatusCode.Forbidden, "work-scope-not-authorized");
}
