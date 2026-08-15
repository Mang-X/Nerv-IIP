using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Application.Auth;

public sealed record WarehouseAssignedResourceExecutionRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string? ActorPrincipalId,
    IReadOnlyCollection<string>? AuthorizedSiteCodes,
    string? ScopeKind,
    string? ScopeId,
    string ResourceOrganizationId,
    string ResourceEnvironmentId,
    string ResourceSiteCode,
    string? AssignedPoolCode,
    string? AssignedOperatorUserId);

/// <summary>
/// Enforces the WMS-owned assignment boundary for completing inbound, outbound,
/// and count resources after the Gateway has supplied trusted principal facts.
/// </summary>
public sealed class WarehouseAssignedResourceExecutionAuthorizer(
    WarehouseWorkScopeAuthorizer workScopeAuthorizer)
{
    public async Task AuthorizeAsync(
        WarehouseAssignedResourceExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = Required(request.OrganizationId, "missing-trusted-organization");
        var environmentId = Required(request.EnvironmentId, "missing-trusted-environment");
        var actorPrincipalId = Required(request.ActorPrincipalId, "missing-trusted-principal");
        var scopeKind = Required(request.ScopeKind, "missing-work-scope-kind");
        var scopeId = Required(request.ScopeId, "missing-work-scope-id");
        if (!string.Equals(
                organizationId,
                request.ResourceOrganizationId,
                StringComparison.Ordinal)
            || !string.Equals(
                environmentId,
                request.ResourceEnvironmentId,
                StringComparison.Ordinal))
        {
            throw WmsAuthorizationException.Forbidden("resource-tenant-mismatch");
        }

        if (string.IsNullOrWhiteSpace(request.AssignedPoolCode))
        {
            throw WmsAuthorizationException.Forbidden("missing-work-pool-assignment");
        }

        var selection = await workScopeAuthorizer.ResolveAsync(
            new WarehouseWorkScopeRequest(
                organizationId,
                environmentId,
                actorPrincipalId,
                request.AuthorizedSiteCodes ?? [],
                scopeKind,
                scopeId,
                request.ResourceSiteCode),
            cancellationToken);
        // 站点范围（IAM 精确站点授权）是整站作业面，读写口径必须一致：站内资源直接放行，
        // 不再按作业池收窄——否则 site 范围「能读不能做」，对本可作业的主体是回归。
        // self / work-pool 语义不变；站点边界在两条路径上都强制。
        var withinSelectedScope =
            selection.SiteCodes.Contains(request.ResourceSiteCode, StringComparer.Ordinal)
            && (selection.SiteWide
                || selection.PoolCodes.Contains(request.AssignedPoolCode, StringComparer.Ordinal));
        if (!withinSelectedScope)
        {
            throw WmsAuthorizationException.Forbidden("resource-outside-selected-work-scope");
        }

        if (!string.IsNullOrWhiteSpace(request.AssignedOperatorUserId)
            && !string.Equals(
                request.AssignedOperatorUserId,
                actorPrincipalId,
                StringComparison.Ordinal))
        {
            throw WmsAuthorizationException.Forbidden("assignment-principal-mismatch");
        }

        if (string.Equals(selection.ScopeKind, "self", StringComparison.Ordinal)
            && !string.Equals(
                request.AssignedOperatorUserId,
                actorPrincipalId,
                StringComparison.Ordinal))
        {
            throw WmsAuthorizationException.Forbidden("resource-not-assigned-to-self");
        }
    }

    private static string Required(string? value, string reason)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw WmsAuthorizationException.Forbidden(reason)
            : normalized;
    }
}
