using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.DataScopes;
using Nerv.IIP.Iam.Web.Application.Roles;

namespace Nerv.IIP.Iam.Web.Application.Users;

public sealed record EffectiveAccessRequest(string UserId, string OrgId, string EnvId);

public sealed record EffectivePermission(
    string PermissionCode,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<PermissionDataScopeGrant> ScopeGrants);

public sealed record EffectiveAccessResponse(
    string UserId, string LoginName, string OrganizationId, string EnvironmentId,
    string Status, int PermissionVersion, IReadOnlyList<RoleResponse> Roles,
    IReadOnlyList<DataScopeResponse> MembershipDataScopes, IReadOnlyList<EffectivePermission> Permissions);

public interface IIamEffectiveAccess
{
    Task<PagedListResponse<UserResponse>> AddRoleNamesAsync(PagedListResponse<UserResponse> page,
        string organizationId, string environmentId, CancellationToken ct);

    Task<EffectiveAccessResponse?> ReadAsync(EffectiveAccessRequest request, CancellationToken ct);
}

public sealed class PostgreSqlIamEffectiveAccess(
    IUserRepository users,
    IMembershipRepository memberships,
    IRoleRepository roleRepository,
    IIamRoleApplicationService roles) : IIamEffectiveAccess
{
    public async Task<PagedListResponse<UserResponse>> AddRoleNamesAsync(
        PagedListResponse<UserResponse> page, string organizationId, string environmentId, CancellationToken ct)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        var names = await roleRepository.ListMemberRoleNamesAsync(
            page.Items.Select(user => new UserId(user.UserId)).ToArray(),
            new OrganizationId(organizationId), new IamEnvironmentId(environmentId), ct);
        return page with
        {
            Items = page.Items.Select(user => user with { RoleNames = names.GetValueOrDefault(user.UserId) ?? [] }).ToArray(),
        };
    }

    public async Task<EffectiveAccessResponse?> ReadAsync(EffectiveAccessRequest request, CancellationToken ct)
    {
        var userId = new UserId(request.UserId);
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        var membership = await memberships.GetByUserIdAndOrgEnvAsync(
            userId, new OrganizationId(request.OrgId), new IamEnvironmentId(request.EnvId), ct);
        var boundRoles = await roles.ResolveRolesAsync(membership?.Roles.Select(x => x.RoleId.Id).ToArray() ?? [], ct);
        var scopes = membership?.DataScopes.Select(x => new DataScopeResponse(x.ScopeType, x.ScopeCode)).ToArray() ?? [];
        return EffectiveAccessProjection.Create(request, user.LoginName, user.Enabled, user.AccountExpiresAtUtc,
            user.LockoutUntilUtc, user.PermissionVersion, membership?.Id.Id, scopes, boundRoles);
    }
}

public sealed class InMemoryIamEffectiveAccess(
    InMemoryIamStore store,
    IIamRoleApplicationService roles) : IIamEffectiveAccess
{
    public Task<PagedListResponse<UserResponse>> AddRoleNamesAsync(
        PagedListResponse<UserResponse> page, string organizationId, string environmentId, CancellationToken ct)
    {
        var names = store.Roles.ToDictionary(role => role.RoleId, role => role.RoleName, StringComparer.Ordinal);
        var items = page.Items.Select(user => user with
        {
            RoleNames = store.GetMembershipRoleIds(user.UserId, organizationId, environmentId)
                .Where(names.ContainsKey)
                .Select(roleId => names[roleId])
                .Order(StringComparer.Ordinal)
                .ToArray(),
        }).ToArray();
        return Task.FromResult(page with { Items = items });
    }

    public async Task<EffectiveAccessResponse?> ReadAsync(EffectiveAccessRequest request, CancellationToken ct)
    {
        var user = store.Users.SingleOrDefault(x => x.UserId == request.UserId);
        if (user is null)
        {
            return null;
        }

        var roleIds = store.GetMembershipRoleIds(request.UserId, request.OrgId, request.EnvId);
        var boundRoles = await roles.ResolveRolesAsync(roleIds, ct);
        var scopes = DataScopeApplicationMapping.ToResponseItems(
            store.GetMembershipDataScopes(request.UserId, request.OrgId, request.EnvId));
        return EffectiveAccessProjection.Create(request, user.LoginName, user.Enabled, user.AccountExpiresAtUtc,
            user.LockoutUntilUtc, user.PermissionVersion,
            InMemoryIamStore.MembershipDataScopeKey(request.UserId, request.OrgId, request.EnvId), scopes, boundRoles);
    }
}

internal static class EffectiveAccessProjection
{
    public static EffectiveAccessResponse Create(
        EffectiveAccessRequest request, string loginName, bool enabled, DateTimeOffset? expiresAt,
        DateTimeOffset? lockoutUntil, int permissionVersion, string? membershipId,
        IReadOnlyList<DataScopeResponse> membershipScopes, IReadOnlyList<RoleResponse> roles)
    {
        var now = DateTimeOffset.UtcNow;
        var status = !enabled ? "disabled" : expiresAt <= now ? "expired" : lockoutUntil > now ? "locked" : "active";
        var permissions = roles
            .SelectMany(role => role.PermissionCodes.Select(code => (Code: code, Role: role)))
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new EffectivePermission(
                group.Key,
                group.Select(x => x.Role.RoleId).ToArray(),
                group.SelectMany(x => Grants("role", x.Role.RoleId, x.Role.DataScopes, group.Key))
                    .Concat(membershipId is null ? [] : Grants("membership", membershipId, membershipScopes, group.Key))
                    .ToArray()))
            .ToArray();

        return new EffectiveAccessResponse(request.UserId, loginName, request.OrgId, request.EnvId,
            status, permissionVersion, roles, membershipScopes, permissions);

        IEnumerable<PermissionDataScopeGrant> Grants(
            string sourceKind, string sourceId, IReadOnlyList<DataScopeResponse> scopes, string permissionCode) =>
            scopes.Select(scope => PermissionDataScopeGrant.FromBinding(
                sourceKind, sourceId, new DataScopeBinding(scope.ScopeType, scope.ScopeCode),
                request.UserId, request.OrgId, permissionCode));
    }
}
