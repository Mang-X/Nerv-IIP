using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayPrincipalWorkContextResolverTests
{
    private const string PermissionCode = BusinessGatewayPermissions.MesWorkOrdersRead;

    [Fact]
    public void Workshop_grants_aggregate_all_authorization_paths_without_duplicate_scopes()
    {
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(
                new AuthorizationScopeGrant("role", "role-a", "workshop", "WS-A", [PermissionCode]),
                new AuthorizationScopeGrant("membership", "membership-a", "workshop", "WS-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.True(result.SelectionAuthorized);
        Assert.Equal(
            ["team:TEAM-A", "work-center:WC-A", "workshop:WS-A"],
            result.AuthorizedScopes.Select(x => $"{x.Kind}:{x.Id}"));
        Assert.All(result.AuthorizedScopes, scope => Assert.Equal(2, scope.AuthorizationPaths.Count));
        Assert.Equal(
            ["membership:membership-a", "role:role-a"],
            result.AuthorizedScopes[0].AuthorizationPaths.Select(x => $"{x.SourceKind}:{x.SourceId}"));
        Assert.DoesNotContain(result.AuthorizedScopes, x => x.Id is "WC-B" or "org-001");
    }

    [Fact]
    public void Organization_site_and_line_hierarchy_follow_frozen_derivation_rules()
    {
        var organization = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(new AuthorizationScopeGrant(
                "role",
                "role-org",
                "organization",
                "org-001",
                [PermissionCode],
                OrganizationWide: true)),
            "org-001",
            PermissionCode,
            null,
            null);
        var site = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(new AuthorizationScopeGrant("role", "role-site", "site", "SITE-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);
        var line = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(new AuthorizationScopeGrant("role", "role-line", "production-line", "LINE-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.Equal(Context().CandidateScopes.Count, organization.AuthorizedScopes.Count);
        Assert.Equal(
            ["site:SITE-A", "team:TEAM-A", "work-center:WC-A", "workshop:WS-A"],
            site.AuthorizedScopes.Select(x => $"{x.Kind}:{x.Id}"));
        var lineScope = Assert.Single(line.AuthorizedScopes);
        Assert.Equal("work-center", lineScope.Kind);
        Assert.Equal("WC-A", lineScope.Id);
        Assert.Equal("production-line-work-center", Assert.Single(lineScope.AuthorizationPaths).Relationship);
    }

    [Fact]
    public void Exact_site_selection_is_authorized_by_a_matching_site_grant()
    {
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(new AuthorizationScopeGrant("role", "role-site", "site", "SITE-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            "site",
            "SITE-A");

        Assert.True(result.SelectionAuthorized);
        var selected = Assert.IsType<BusinessConsoleAuthorizedWorkScope>(result.SelectedScope);
        Assert.Equal("site", selected.Kind);
        Assert.Equal("SITE-A", selected.Id);
        Assert.Equal("exact", Assert.Single(selected.AuthorizationPaths).Relationship);
    }

    [Fact]
    public void Exact_team_and_work_center_grants_do_not_infer_each_other()
    {
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(
                new AuthorizationScopeGrant("role", "role-team", "team", "TEAM-A", [PermissionCode]),
                new AuthorizationScopeGrant("role", "role-work-center", "work-center", "WC-B", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.Equal(
            ["team:TEAM-A", "work-center:WC-B"],
            result.AuthorizedScopes.Select(x => $"{x.Kind}:{x.Id}"));
    }

    [Fact]
    public void Workshop_grant_never_derives_self_for_a_worker_spanning_multiple_workshops()
    {
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(new AuthorizationScopeGrant("role", "role-workshop", "workshop", "WS-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.DoesNotContain(result.AuthorizedScopes, x => x.Kind == "self");
        Assert.Contains(result.AuthorizedScopes, x => x.Kind == "team" && x.Id == "TEAM-A");
    }

    [Fact]
    public void Deny_all_has_priority_and_unauthorized_selection_fails_closed()
    {
        var denied = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(
                new AuthorizationScopeGrant(
                    "role",
                    "role-org",
                    "organization",
                    "org-001",
                    [PermissionCode],
                    OrganizationWide: true),
                dataScope: new AuthorizationDataScope([], [], [], DenyAll: true)),
            "org-001",
            PermissionCode,
            null,
            null);
        var selection = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(new AuthorizationScopeGrant("role", "role-team", "team", "TEAM-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            "work-center",
            "WC-A");

        Assert.Empty(denied.AuthorizedScopes);
        Assert.Empty(denied.AvailableScopeKinds);
        Assert.Empty(denied.CandidateScopes);
        Assert.Empty(denied.CandidateScopeKinds);
        Assert.False(selection.SelectionAuthorized);
        Assert.Null(selection.SelectedScope);
    }

    [Fact]
    public void Mismatched_permission_and_unknown_grant_kind_never_authorize_candidates()
    {
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(
                new AuthorizationScopeGrant("role", "role-other-permission", "team", "TEAM-A", ["business.quality.inspection-records.read"]),
                new AuthorizationScopeGrant("role", "role-unknown-kind", "cell", "TEAM-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.Empty(result.AuthorizedScopes);
        Assert.Empty(result.AvailableScopeKinds);
    }

    [Fact]
    public void Null_or_unauditable_grants_are_ignored_instead_of_authorizing_or_throwing()
    {
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            Context(),
            Authorization(
                [
                    null!,
                    new AuthorizationScopeGrant("", "role-empty-source", "team", "TEAM-A", [PermissionCode]),
                    new AuthorizationScopeGrant("role", "", "team", "TEAM-A", [PermissionCode]),
                    new AuthorizationScopeGrant("wire-drift", "source-a", "team", "TEAM-A", [PermissionCode]),
                ],
                null),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.Empty(result.AuthorizedScopes);
        Assert.Empty(result.AvailableScopeKinds);
    }

    [Fact]
    public void Organization_grant_ignores_unknown_and_malformed_wire_candidates()
    {
        var context = Context() with
        {
            CandidateScopes =
            [
                .. Context().CandidateScopes,
                Scope("cell", "CELL-A", "单元 A", "wire-drift"),
                Scope(null!, "TEAM-B", "坏类型", "wire-drift"),
                Scope("team", null!, "坏标识", "wire-drift"),
            ],
        };
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            Authorization(new AuthorizationScopeGrant(
                "role",
                "role-org",
                "organization",
                "org-001",
                [PermissionCode],
                OrganizationWide: true)),
            "org-001",
            PermissionCode,
            null,
            null);
        var missingCollection = PrincipalWorkContextAuthorizationResolver.Resolve(
            context with { CandidateScopes = null! },
            Authorization(new AuthorizationScopeGrant(
                "role",
                "role-org",
                "organization",
                "org-001",
                [PermissionCode],
                OrganizationWide: true)),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.Equal(Context().CandidateScopes.Count, result.AuthorizedScopes.Count);
        Assert.Equal(Context().CandidateScopes.Count, result.CandidateScopes.Count);
        Assert.DoesNotContain(result.AuthorizedScopes, x => x.Kind == "cell");
        var syntheticOrganization = Assert.Single(missingCollection.AuthorizedScopes);
        Assert.Equal("organization", syntheticOrganization.Kind);
    }

    [Fact]
    public void Duplicate_candidates_are_merged_before_authorization_and_selection()
    {
        var context = Context() with
        {
            CandidateScopes =
            [
                .. Context().CandidateScopes,
                Scope(
                    "work-center",
                    "WC-A",
                    "加工中心",
                    "workshop-covered",
                    ("workshop", "WS-A"),
                    ("site", "SITE-A"),
                    ("production-line", "LINE-A")),
            ],
        };
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            Authorization(new AuthorizationScopeGrant("role", "role-work-center", "work-center", "WC-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            "work-center",
            "WC-A");

        var scope = Assert.Single(result.AuthorizedScopes);
        Assert.True(result.SelectionAuthorized);
        Assert.Same(scope, result.SelectedScope);
    }

    [Fact]
    public void Conflicting_duplicate_candidate_fails_closed()
    {
        var context = Context() with
        {
            CandidateScopes =
            [
                .. Context().CandidateScopes,
                Scope("work-center", "WC-A", "冲突名称", "wire-drift", ("workshop", "WS-A")),
            ],
        };
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            Authorization(new AuthorizationScopeGrant("role", "role-work-center", "work-center", "WC-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.DoesNotContain(result.AuthorizedScopes, x => x.Kind == "work-center" && x.Id == "WC-A");
    }

    [Fact]
    public void Duplicate_candidate_with_different_ancestors_cannot_gain_scope()
    {
        var context = Context() with
        {
            CandidateScopes =
            [
                .. Context().CandidateScopes,
                Scope(
                    "work-center",
                    "WC-B",
                    "装配中心",
                    "workshop-covered",
                    ("workshop", "WS-A"),
                    ("site", "SITE-A"),
                    ("production-line", "LINE-B")),
            ],
        };
        var result = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            Authorization(new AuthorizationScopeGrant("role", "role-workshop", "workshop", "WS-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        Assert.DoesNotContain(result.CandidateScopes, x => x.Kind == "work-center" && x.Id == "WC-B");
        Assert.DoesNotContain(result.AuthorizedScopes, x => x.Kind == "work-center" && x.Id == "WC-B");
    }

    [Fact]
    public void Missing_worker_only_gets_synthetic_organization_scope_from_matching_organization_grant()
    {
        var context = Context() with
        {
            ResolutionStatus = "worker-not-mapped",
            Worker = null,
            CandidateScopes = [],
            CandidateScopeKinds = [],
            Issues = ["worker-not-mapped"],
        };
        var organization = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            Authorization(new AuthorizationScopeGrant(
                "role",
                "role-org",
                "organization",
                "org-001",
                [PermissionCode],
                OrganizationWide: true)),
            "org-001",
            PermissionCode,
            null,
            null);
        var workshop = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            Authorization(new AuthorizationScopeGrant("role", "role-workshop", "workshop", "WS-A", [PermissionCode])),
            "org-001",
            PermissionCode,
            null,
            null);

        var scope = Assert.Single(organization.AuthorizedScopes);
        Assert.Equal("organization", scope.Kind);
        Assert.Equal("org-001", scope.Id);
        Assert.Equal(["organization"], organization.CandidateScopeKinds);
        Assert.Empty(workshop.AuthorizedScopes);
    }

    private static BusinessMasterDataPrincipalWorkContextResponse Context() =>
        new(
            "ready-with-gaps",
            new BusinessMasterDataWorkContextWorker(
                "worker-id",
                "user-001",
                "EMP-001",
                "操作工",
                null,
                null,
                "机加操作工",
                "active"),
            [],
            [],
            [],
            [],
            [],
            [
                Scope("self", "user-001", "操作工", "worker-mapping", ("workshop", "WS-A"), ("site", "SITE-A")),
                Scope("team", "TEAM-A", "甲班", "active-membership", ("workshop", "WS-A"), ("site", "SITE-A")),
                Scope("work-center", "WC-A", "加工中心", "workshop-covered", ("workshop", "WS-A"), ("site", "SITE-A"), ("production-line", "LINE-A")),
                Scope("work-center", "WC-B", "装配中心", "workshop-covered", ("workshop", "WS-B"), ("site", "SITE-B"), ("production-line", "LINE-B")),
                Scope("workshop", "WS-A", "机加车间", "active-team-workshop", ("site", "SITE-A")),
                Scope("site", "SITE-A", "南京工厂", "resolved-site", ("organization", "org-001")),
                Scope("organization", "org-001", "当前组织", "principal-membership"),
            ],
            ["organization", "self", "site", "team", "work-center", "workshop"],
            ["position-master-not-modeled"]);

    private static BusinessMasterDataWorkContextCandidateScope Scope(
        string kind,
        string id,
        string name,
        string relationship,
        params (string Kind, string Id)[] ancestors) =>
        new(
            kind,
            id,
            name,
            relationship,
            ancestors.Select(x => new BusinessMasterDataWorkContextScopeAncestor(x.Kind, x.Id)).ToArray());

    private static BusinessGatewayAuthorizationResult Authorization(
        AuthorizationScopeGrant grant,
        AuthorizationDataScope? dataScope = null) =>
        Authorization([grant], dataScope);

    private static BusinessGatewayAuthorizationResult Authorization(
        AuthorizationScopeGrant first,
        AuthorizationScopeGrant second,
        AuthorizationDataScope? dataScope = null) =>
        Authorization([first, second], dataScope);

    private static BusinessGatewayAuthorizationResult Authorization(
        IReadOnlyCollection<AuthorizationScopeGrant> grants,
        AuthorizationDataScope? dataScope) =>
        BusinessGatewayAuthorizationResult.Allowed(
            "user-001",
            "user",
            "operator",
            dataScope,
            grants,
            [new AuthorizationRole("role-worker", "一线操作工")]);
}
