using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Permissions;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Roles;

public sealed record RoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record CreateRoleRequest(string? RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record PatchRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

public interface IIamRoleApplicationService
{
    Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken);

    Task<RoleResponse> CreateRoleAsync(
        string? roleName,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken);

    Task<RoleResponse> PatchRolePermissionsAsync(
        string roleId,
        IReadOnlyList<string> permissionCodes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken);
}

public sealed class InMemoryIamRoleApplicationService(InMemoryIamStore store) : IIamRoleApplicationService
{
    public Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var roles = store.Roles
            .Where(role => string.IsNullOrWhiteSpace(options.FilterSearch)
                || role.RoleId.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || role.RoleName.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || role.PermissionCodes.Any(code => code.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)))
            .Select(ToResponse)
            .ApplyRoleSort(options)
            .ToPagedResponse(options);
        return Task.FromResult(roles);
    }

    public Task<RoleResponse> CreateRoleAsync(
        string? roleName,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        var trimmedRoleName = RoleNameValidation.NormalizeRoleName(roleName);
        if (store.RoleNameExists(trimmedRoleName))
        {
            throw new KnownException($"Role name '{trimmedRoleName}' is already used.");
        }

        var seededCodes = IamPermissionCatalog.EnsureSeeded(permissionCodes ?? []);
        return Task.FromResult(ToResponse(store.CreateRole(trimmedRoleName, seededCodes)));
    }

    public Task<RoleResponse> PatchRolePermissionsAsync(
        string roleId,
        IReadOnlyList<string> permissionCodes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        _ = auditContext;
        var seededCodes = IamPermissionCatalog.EnsureSeeded(permissionCodes ?? []);
        try
        {
            return Task.FromResult(ToResponse(store.ReplaceRolePermissions(roleId, seededCodes)));
        }
        catch (InvalidOperationException)
        {
            throw new KnownException($"Role '{roleId}' was not found.");
        }
    }

    private static RoleResponse ToResponse(RoleFact role)
    {
        return new RoleResponse(
            role.RoleId,
            role.RoleName,
            role.PermissionCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

}

public sealed class PostgreSqlIamRoleApplicationService(
    IRoleRepository repository,
    ISecurityAuditRecorder securityAudit) : IIamRoleApplicationService
{
    public async Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var roles = await repository.ListNotDeletedAsync(cancellationToken);
        return roles
            .Where(role => string.IsNullOrWhiteSpace(options.FilterSearch)
                || role.Id.Id.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || role.RoleName.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || role.Permissions.Any(permission => permission.PermissionCode.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)))
            .Select(ToResponse)
            .ApplyRoleSort(options)
            .ToPagedResponse(options);
    }

    public async Task<RoleResponse> CreateRoleAsync(
        string? roleName,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        var trimmedRoleName = RoleNameValidation.NormalizeRoleName(roleName);
        var seededCodes = IamPermissionCatalog.EnsureSeeded(permissionCodes ?? []);
        if (await repository.GetByNameAsync(trimmedRoleName, cancellationToken) is not null)
        {
            throw new KnownException($"Role name '{trimmedRoleName}' is already used.");
        }

        var role = new Role(
            new RoleId($"role-{Guid.CreateVersion7():N}"),
            trimmedRoleName,
            seededCodes);
        try
        {
            await repository.AddAndSaveAsync(role, cancellationToken);
        }
        catch (DuplicateRoleNameException)
        {
            throw new KnownException($"Role name '{trimmedRoleName}' is already used.");
        }

        return ToResponse(role);
    }

    public async Task<RoleResponse> PatchRolePermissionsAsync(
        string roleId,
        IReadOnlyList<string> permissionCodes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        var role = await repository.GetByIdAsync(new RoleId(roleId), cancellationToken)
            ?? throw new KnownException($"Role '{roleId}' was not found.");
        var seededCodes = IamPermissionCatalog.EnsureSeeded(permissionCodes ?? []);
        var before = role.Permissions.Select(x => x.PermissionCode).Order(StringComparer.Ordinal).ToArray();
        role.ReplacePermissions(seededCodes);
        var after = role.Permissions.Select(x => x.PermissionCode).Order(StringComparer.Ordinal).ToArray();
        await securityAudit.RecordAsync(
            auditContext ?? new SecurityAuditContext("unknown", Guid.CreateVersion7().ToString("N"), null, "unknown", "unknown"),
            "iam.role.permissions.changed",
            "role",
            role.Id.Id,
            "success",
            new { before, after },
            DateTimeOffset.UtcNow,
            cancellationToken);
        return ToResponse(role);
    }

    private static RoleResponse ToResponse(Role role)
    {
        return new RoleResponse(
            role.Id.Id,
            role.RoleName,
            role.Permissions.Select(x => x.PermissionCode).OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

}

internal static class RoleListSorting
{
    public static IEnumerable<RoleResponse> ApplyRoleSort(this IEnumerable<RoleResponse> roles, IamListQueryOptions options)
    {
        return (options.SortBy?.ToLowerInvariant(), options.IsDescending) switch
        {
            ("roleid", true) => roles.OrderByDescending(x => x.RoleId, StringComparer.Ordinal),
            ("roleid", false) => roles.OrderBy(x => x.RoleId, StringComparer.Ordinal),
            ("rolename", true) => roles.OrderByDescending(x => x.RoleName, StringComparer.Ordinal),
            _ => roles.OrderBy(x => x.RoleName, StringComparer.Ordinal)
        };
    }
}

internal static class RoleNameValidation
{
    public static string NormalizeRoleName(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new KnownException("Role name is required.");
        }

        var trimmedRoleName = roleName.Trim();
        if (trimmedRoleName.Length > 128)
        {
            throw new KnownException("Role name must be 128 characters or fewer.");
        }

        return trimmedRoleName;
    }
}
