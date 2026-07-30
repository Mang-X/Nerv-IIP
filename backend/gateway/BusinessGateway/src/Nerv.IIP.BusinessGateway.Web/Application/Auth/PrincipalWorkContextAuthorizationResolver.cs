using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed record PrincipalWorkContextAuthorizationResolution(
    IReadOnlyList<BusinessMasterDataWorkContextCandidateScope> CandidateScopes,
    IReadOnlyList<string> CandidateScopeKinds,
    IReadOnlyList<BusinessConsoleAuthorizedWorkScope> AuthorizedScopes,
    IReadOnlyList<string> AvailableScopeKinds,
    BusinessConsoleAuthorizedWorkScope? SelectedScope,
    bool SelectionAuthorized);

public static class PrincipalWorkContextAuthorizationResolver
{
    public static PrincipalWorkContextAuthorizationResolution Resolve(
        BusinessMasterDataPrincipalWorkContextResponse context,
        BusinessGatewayAuthorizationResult authorization,
        string organizationId,
        string permissionCode,
        string? requestedScopeKind,
        string? requestedScopeId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);
        var hasCompleteSelection = !string.IsNullOrWhiteSpace(requestedScopeKind)
            && !string.IsNullOrWhiteSpace(requestedScopeId);
        var hasPartialSelection = string.IsNullOrWhiteSpace(requestedScopeKind)
            != string.IsNullOrWhiteSpace(requestedScopeId);
        if (!authorization.IsAllowed || authorization.DataScope?.DenyAll == true)
        {
            return new PrincipalWorkContextAuthorizationResolution(
                [],
                [],
                [],
                [],
                null,
                !hasCompleteSelection && !hasPartialSelection);
        }

        var grants = TrustedGrants(authorization, permissionCode);
        var candidates = NormalizeCandidates(context.CandidateScopes ?? []).ToList();
        if (!candidates.Any(x =>
                string.Equals(x.Kind, "organization", StringComparison.Ordinal)
                && string.Equals(x.Id, organizationId, StringComparison.Ordinal))
            && grants.Any(x => IsMatchingOrganizationGrant(x, organizationId)))
        {
            candidates.Add(new BusinessMasterDataWorkContextCandidateScope(
                "organization",
                organizationId,
                "当前组织",
                "iam-organization-grant",
                []));
        }

        var candidateKinds = candidates
            .Select(x => x.Kind)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var authorized = AuthorizeCandidates(candidates, grants, organizationId);
        var availableKinds = authorized
            .Select(x => x.Kind)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (hasPartialSelection)
        {
            return new PrincipalWorkContextAuthorizationResolution(
                candidates,
                candidateKinds,
                authorized,
                availableKinds,
                null,
                false);
        }

        if (!hasCompleteSelection)
        {
            return new PrincipalWorkContextAuthorizationResolution(
                candidates,
                candidateKinds,
                authorized,
                availableKinds,
                null,
                true);
        }

        var selected = authorized.SingleOrDefault(x =>
            string.Equals(x.Kind, requestedScopeKind!.Trim(), StringComparison.Ordinal)
            && string.Equals(x.Id, requestedScopeId!.Trim(), StringComparison.Ordinal));
        return new PrincipalWorkContextAuthorizationResolution(
            candidates,
            candidateKinds,
            authorized,
            availableKinds,
            selected,
            selected is not null);
    }

    public static IReadOnlyList<BusinessConsoleAuthorizedWorkScope> ResolveSiteCandidates(
        IReadOnlyCollection<BusinessConsoleResourceItem> siteDirectory,
        BusinessGatewayAuthorizationResult authorization,
        string organizationId,
        string permissionCode)
    {
        ArgumentNullException.ThrowIfNull(siteDirectory);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!authorization.IsAllowed || authorization.DataScope?.DenyAll == true)
        {
            return [];
        }

        var siteCandidates = siteDirectory
            .Where(site =>
                site is not null
                && site.Active
                && string.Equals(
                    site.ResourceType?.Trim(),
                    "site",
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(site.Code)
                && !string.IsNullOrWhiteSpace(site.DisplayName))
            .Select(site => new BusinessMasterDataWorkContextCandidateScope(
                "site",
                site.Code,
                site.DisplayName,
                "wms-site-candidate",
                [new BusinessMasterDataWorkContextScopeAncestor("organization", organizationId)]))
            .ToArray();
        return AuthorizeCandidates(
            NormalizeCandidates(siteCandidates, allowSite: true),
            TrustedGrants(authorization, permissionCode),
            organizationId);
    }

    private static AuthorizationScopeGrant[] TrustedGrants(
        BusinessGatewayAuthorizationResult authorization,
        string permissionCode) =>
        (authorization.ScopeGrants ?? [])
            .Where(x =>
                x is not null
                && IsKnownGrantSource(x.SourceKind)
                && !string.IsNullOrWhiteSpace(x.SourceId)
                && !string.IsNullOrWhiteSpace(x.ScopeKind)
                && !string.IsNullOrWhiteSpace(x.ScopeId)
                && x.ApplicablePermissionCodes?.Contains(permissionCode, StringComparer.Ordinal) == true
                && IsKnownGrantKind(x.ScopeKind))
            .ToArray();

    private static BusinessConsoleAuthorizedWorkScope[] AuthorizeCandidates(
        IReadOnlyCollection<BusinessMasterDataWorkContextCandidateScope> candidates,
        IReadOnlyCollection<AuthorizationScopeGrant> grants,
        string organizationId) =>
        candidates
            .Select(candidate => Authorize(candidate, grants, organizationId))
            .Where(x => x is not null)
            .Cast<BusinessConsoleAuthorizedWorkScope>()
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

    private static BusinessConsoleAuthorizedWorkScope? Authorize(
        BusinessMasterDataWorkContextCandidateScope candidate,
        IReadOnlyCollection<AuthorizationScopeGrant> grants,
        string organizationId)
    {
        var paths = grants
            .Select(grant => CreatePath(candidate, grant, organizationId))
            .Where(x => x is not null)
            .Cast<BusinessConsoleWorkScopeAuthorizationPath>()
            .OrderBy(x => x.SourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal)
            .ThenBy(x => x.GrantScopeKind, StringComparer.Ordinal)
            .ThenBy(x => x.GrantScopeId, StringComparer.Ordinal)
            .ThenBy(x => x.Relationship, StringComparer.Ordinal)
            .GroupBy(
                x => string.Join(
                    '\u001f',
                    x.SourceKind,
                    x.SourceId,
                    x.GrantScopeKind,
                    x.GrantScopeId,
                    x.Relationship,
                    string.Join('\u001e', x.ApplicablePermissionCodes.Order(StringComparer.Ordinal))),
                StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
        return paths.Length == 0
            ? null
            : new BusinessConsoleAuthorizedWorkScope(
                candidate.Kind,
                candidate.Id,
                candidate.DisplayName,
                candidate.Relationship,
                paths);
    }

    private static IReadOnlyCollection<BusinessMasterDataWorkContextCandidateScope> NormalizeCandidates(
        IReadOnlyCollection<BusinessMasterDataWorkContextCandidateScope> candidates,
        bool allowSite = false) =>
        candidates
            .Where(x =>
                x is not null
                && !string.IsNullOrWhiteSpace(x.Kind)
                && !string.IsNullOrWhiteSpace(x.Id)
                && (IsKnownCandidateKind(x.Kind)
                    || allowSite && string.Equals(
                        x.Kind.Trim(),
                        "site",
                        StringComparison.OrdinalIgnoreCase)))
            .GroupBy(
                x => $"{x.Kind.Trim().ToLowerInvariant()}\u001f{x.Id.Trim()}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var displayNames = group
                    .Select(x => x.DisplayName?.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var relationships = group
                    .Select(x => x.Relationship?.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (displayNames.Length != 1
                    || string.IsNullOrWhiteSpace(displayNames[0])
                    || relationships.Length != 1
                    || string.IsNullOrWhiteSpace(relationships[0]))
                {
                    return null;
                }

                var normalizedAncestorSets = group
                    .Select(x => NormalizeAncestors(x.Ancestors ?? []))
                    .ToArray();
                var ancestorSignatures = normalizedAncestorSets
                    .Select(x => string.Join(
                        '\u001e',
                        x.Select(ancestor => $"{ancestor.Kind}\u001f{ancestor.Id}")))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (ancestorSignatures.Length != 1)
                {
                    return null;
                }

                var first = group.First();
                return new BusinessMasterDataWorkContextCandidateScope(
                    first.Kind.Trim().ToLowerInvariant(),
                    first.Id.Trim(),
                    displayNames[0]!,
                    relationships[0]!,
                    normalizedAncestorSets[0]);
            })
            .Where(x => x is not null)
            .Cast<BusinessMasterDataWorkContextCandidateScope>()
            .ToArray();

    private static BusinessMasterDataWorkContextScopeAncestor[] NormalizeAncestors(
        IReadOnlyCollection<BusinessMasterDataWorkContextScopeAncestor> ancestors) =>
        ancestors
            .Where(x =>
                x is not null
                && !string.IsNullOrWhiteSpace(x.Kind)
                && !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => new BusinessMasterDataWorkContextScopeAncestor(
                x.Kind.Trim().ToLowerInvariant(),
                x.Id.Trim()))
            .Distinct()
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

    private static BusinessConsoleWorkScopeAuthorizationPath? CreatePath(
        BusinessMasterDataWorkContextCandidateScope candidate,
        AuthorizationScopeGrant grant,
        string organizationId)
    {
        var grantKind = grant.ScopeKind.Trim().ToLowerInvariant();
        var relationship = grantKind switch
        {
            "organization" when IsMatchingOrganizationGrant(grant, organizationId) =>
                string.Equals(candidate.Kind, "organization", StringComparison.Ordinal)
                    && string.Equals(candidate.Id, organizationId, StringComparison.Ordinal)
                    ? "exact"
                    : "organization-descendant",
            "self" when Exact(candidate, "self", grant.ScopeId) => "exact",
            "team" when Exact(candidate, "team", grant.ScopeId) => "exact",
            "work-center" when Exact(candidate, "work-center", grant.ScopeId) => "exact",
            "workshop" when Exact(candidate, "workshop", grant.ScopeId) => "exact",
            "site" when Exact(candidate, "site", grant.ScopeId) => "exact",
            "workshop" when candidate.Kind is "team" or "work-center"
                && HasAncestor(candidate, "workshop", grant.ScopeId) => "workshop-descendant",
            "site" when candidate.Kind is "team" or "work-center" or "workshop"
                && HasAncestor(candidate, "site", grant.ScopeId) => "site-descendant",
            "production-line" when string.Equals(candidate.Kind, "work-center", StringComparison.Ordinal)
                && HasAncestor(candidate, "production-line", grant.ScopeId) => "production-line-work-center",
            _ => null,
        };
        return relationship is null
            ? null
            : new BusinessConsoleWorkScopeAuthorizationPath(
                grant.SourceKind,
                grant.SourceId,
                grantKind,
                grant.ScopeId,
                relationship,
                grant.ApplicablePermissionCodes
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
    }

    private static bool Exact(
        BusinessMasterDataWorkContextCandidateScope candidate,
        string kind,
        string id) =>
        string.Equals(candidate.Kind, kind, StringComparison.Ordinal)
        && string.Equals(candidate.Id, id, StringComparison.Ordinal);

    private static bool HasAncestor(
        BusinessMasterDataWorkContextCandidateScope candidate,
        string kind,
        string id) =>
        candidate.Ancestors?.Any(x =>
            string.Equals(x.Kind, kind, StringComparison.Ordinal)
            && string.Equals(x.Id, id, StringComparison.Ordinal)) == true;

    private static bool IsMatchingOrganizationGrant(AuthorizationScopeGrant grant, string organizationId) =>
        grant.OrganizationWide
        && string.Equals(grant.ScopeKind, "organization", StringComparison.OrdinalIgnoreCase)
        && string.Equals(grant.ScopeId, organizationId, StringComparison.Ordinal);

    private static bool IsKnownGrantKind(string kind) =>
        kind.Trim().ToLowerInvariant() is
            "self" or "team" or "work-center" or "workshop" or "organization" or "site" or "production-line";

    private static bool IsKnownGrantSource(string? sourceKind) =>
        sourceKind?.Trim().ToLowerInvariant() is "role" or "membership";

    private static bool IsKnownCandidateKind(string kind) =>
        kind.Trim().ToLowerInvariant() is
            "self" or "team" or "work-center" or "workshop" or "organization";
}
