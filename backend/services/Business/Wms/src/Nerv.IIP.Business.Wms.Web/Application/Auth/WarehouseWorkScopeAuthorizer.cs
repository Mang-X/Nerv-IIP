using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Application.Auth;

public sealed record WarehouseWorkScopeRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    string? SiteCode);

public sealed record WarehouseWorkScopeSelection(
    string ActorPrincipalId,
    string ScopeKind,
    string ScopeId,
    string? AssignedOperatorUserId,
    IReadOnlyList<string> PoolCodes,
    IReadOnlyList<string> SiteCodes);

public sealed record WarehouseAssignmentAuthorizationRequest(
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string SiteCode,
    string PoolCode,
    string? OperatorPrincipalId);

public sealed record WarehouseAssignmentAuthorizationResult(
    string AssignerPrincipalId,
    string SiteCode,
    string PoolCode,
    string? OperatorPrincipalId);

/// <summary>
/// Applies the WMS-owned work-pool qualification boundary after IAM has supplied
/// the caller's trusted principal and exact-site grants.
/// </summary>
public sealed class WarehouseWorkScopeAuthorizer(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<WarehouseWorkScopeSelection> ResolveAsync(
        WarehouseWorkScopeRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = Required(request.OrganizationId, nameof(request.OrganizationId));
        var environmentId = Required(request.EnvironmentId, nameof(request.EnvironmentId));
        var actorPrincipalId = Required(
            request.ActorPrincipalId,
            nameof(request.ActorPrincipalId));
        var scopeKind = Required(request.ScopeKind, nameof(request.ScopeKind)).ToLowerInvariant();
        var scopeId = Required(request.ScopeId, nameof(request.ScopeId));
        var authorizedSites = NormalizeAuthorizedSites(request.AuthorizedSiteCodes);
        var requestedSite = Optional(request.SiteCode);

        if (requestedSite is not null && !authorizedSites.Contains(requestedSite))
        {
            throw WmsAuthorizationException.Forbidden("site-outside-exact-grant");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var effectiveMemberships = await (
            from membership in dbContext.WarehouseWorkPoolMemberships.AsNoTracking()
            join pool in dbContext.WarehouseWorkPools.AsNoTracking()
                on new
                {
                    membership.OrganizationId,
                    membership.EnvironmentId,
                    membership.PoolCode,
                }
                equals new
                {
                    pool.OrganizationId,
                    pool.EnvironmentId,
                    pool.PoolCode,
                }
            where membership.OrganizationId == organizationId
                && membership.EnvironmentId == environmentId
                && membership.PrincipalId == actorPrincipalId
                && membership.Active
                && membership.EffectiveFromUtc <= now
                && (membership.EffectiveToUtc == null || now < membership.EffectiveToUtc)
                && pool.Active
                && authorizedSites.Contains(pool.SiteCode)
                && (requestedSite == null || pool.SiteCode == requestedSite)
            select new EffectiveMembership(pool.PoolCode, pool.SiteCode))
            .Distinct()
            .ToListAsync(cancellationToken);

        if (effectiveMemberships.Count == 0)
        {
            throw WmsAuthorizationException.Forbidden("no-effective-work-pool-membership");
        }

        return scopeKind switch
        {
            "self" => ResolveSelf(actorPrincipalId, scopeId, effectiveMemberships),
            "work-pool" => ResolvePool(actorPrincipalId, scopeId, effectiveMemberships),
            "site" => ResolveSite(actorPrincipalId, scopeId, effectiveMemberships, authorizedSites),
            _ => throw WmsAuthorizationException.Forbidden("unsupported-work-scope"),
        };
    }

    public async Task<WarehouseAssignmentAuthorizationResult> AuthorizeAssignmentAsync(
        WarehouseAssignmentAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = Required(request.OrganizationId, nameof(request.OrganizationId));
        var environmentId = Required(request.EnvironmentId, nameof(request.EnvironmentId));
        var assignerPrincipalId = Required(
            request.AssignerPrincipalId,
            nameof(request.AssignerPrincipalId));
        var siteCode = Required(request.SiteCode, nameof(request.SiteCode));
        var poolCode = Required(request.PoolCode, nameof(request.PoolCode));
        var operatorPrincipalId = Optional(request.OperatorPrincipalId);
        var authorizedSites = NormalizeAuthorizedSites(request.AuthorizedSiteCodes);

        if (!authorizedSites.Contains(siteCode))
        {
            throw WmsAuthorizationException.Forbidden("site-outside-exact-grant");
        }

        var poolExists = await dbContext.WarehouseWorkPools.AsNoTracking()
            .AnyAsync(
                pool => pool.OrganizationId == organizationId
                    && pool.EnvironmentId == environmentId
                    && pool.PoolCode == poolCode
                    && pool.SiteCode == siteCode
                    && pool.Active,
                cancellationToken);
        if (!poolExists)
        {
            throw WmsAuthorizationException.Forbidden("inactive-or-cross-site-work-pool");
        }

        if (operatorPrincipalId is not null)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var effectiveMember = await dbContext.WarehouseWorkPoolMemberships.AsNoTracking()
                .AnyAsync(
                    membership => membership.OrganizationId == organizationId
                        && membership.EnvironmentId == environmentId
                        && membership.PoolCode == poolCode
                        && membership.PrincipalId == operatorPrincipalId
                        && membership.Active
                        && membership.EffectiveFromUtc <= now
                        && (membership.EffectiveToUtc == null
                            || now < membership.EffectiveToUtc),
                    cancellationToken);
            if (!effectiveMember)
            {
                throw WmsAuthorizationException.Forbidden(
                    "target-operator-not-effective-pool-member");
            }
        }

        return new WarehouseAssignmentAuthorizationResult(
            assignerPrincipalId,
            siteCode,
            poolCode,
            operatorPrincipalId);
    }

    private static WarehouseWorkScopeSelection ResolveSelf(
        string actorPrincipalId,
        string scopeId,
        IReadOnlyCollection<EffectiveMembership> memberships)
    {
        if (!string.Equals(scopeId, actorPrincipalId, StringComparison.Ordinal))
        {
            throw WmsAuthorizationException.Forbidden("self-scope-principal-mismatch");
        }

        return Selection(
            actorPrincipalId,
            "self",
            scopeId,
            actorPrincipalId,
            memberships);
    }

    private static WarehouseWorkScopeSelection ResolvePool(
        string actorPrincipalId,
        string scopeId,
        IReadOnlyCollection<EffectiveMembership> memberships)
    {
        var selected = memberships
            .Where(membership =>
                string.Equals(membership.PoolCode, scopeId, StringComparison.Ordinal))
            .ToArray();
        if (selected.Length == 0)
        {
            throw WmsAuthorizationException.Forbidden("work-pool-membership-mismatch");
        }

        return Selection(
            actorPrincipalId,
            "work-pool",
            scopeId,
            AssignedOperatorUserId: null,
            selected);
    }

    private static WarehouseWorkScopeSelection ResolveSite(
        string actorPrincipalId,
        string scopeId,
        IReadOnlyCollection<EffectiveMembership> memberships,
        IReadOnlySet<string> authorizedSites)
    {
        if (!authorizedSites.Contains(scopeId))
        {
            throw WmsAuthorizationException.Forbidden("site-outside-exact-grant");
        }

        var selected = memberships
            .Where(membership =>
                string.Equals(membership.SiteCode, scopeId, StringComparison.Ordinal))
            .ToArray();
        if (selected.Length == 0)
        {
            throw WmsAuthorizationException.Forbidden("site-membership-mismatch");
        }

        return Selection(
            actorPrincipalId,
            "site",
            scopeId,
            AssignedOperatorUserId: null,
            selected);
    }

    private static WarehouseWorkScopeSelection Selection(
        string actorPrincipalId,
        string scopeKind,
        string scopeId,
        string? AssignedOperatorUserId,
        IReadOnlyCollection<EffectiveMembership> memberships) =>
        new(
            actorPrincipalId,
            scopeKind,
            scopeId,
            AssignedOperatorUserId,
            memberships
                .Select(membership => membership.PoolCode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            memberships
                .Select(membership => membership.SiteCode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static HashSet<string> NormalizeAuthorizedSites(
        IReadOnlyCollection<string>? values)
    {
        var normalized = values?
            .Select(Optional)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal)
            ?? [];
        if (normalized.Count == 0)
        {
            throw WmsAuthorizationException.Forbidden("missing-exact-site-grant");
        }

        return normalized;
    }

    private static string Required(string? value, string parameterName) =>
        Optional(value)
        ?? throw new ArgumentException($"{parameterName} is required.", parameterName);

    private static string? Optional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record EffectiveMembership(string PoolCode, string SiteCode);
}
