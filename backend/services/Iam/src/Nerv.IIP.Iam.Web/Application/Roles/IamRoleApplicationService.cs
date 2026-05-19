using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;

namespace Nerv.IIP.Iam.Web.Application.Roles;

public sealed record RoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record RoleMutationResponse(string RoleId);
public sealed record RoleMutationResult(bool IsImplemented, RoleMutationResponse? Response, string? Detail)
{
    public static RoleMutationResult Implemented(RoleMutationResponse response) => new(true, response, null);
    public static RoleMutationResult NotImplemented(string detail) => new(false, null, detail);
}

public interface IIamRoleApplicationService
{
    Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken);

    Task<RoleMutationResult> CreateRoleAsync(CancellationToken cancellationToken);

    Task<RoleMutationResult> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken);
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
            .Select(x => new RoleResponse(x.RoleId, x.RoleName, x.PermissionCodes.OrderBy(code => code).ToArray()))
            .ApplyRoleSort(options)
            .ToPagedResponse(options);
        return Task.FromResult(roles);
    }

    public Task<RoleMutationResult> CreateRoleAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(RoleMutationResult.Implemented(new RoleMutationResponse("role-placeholder")));
    }

    public Task<RoleMutationResult> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken)
    {
        return Task.FromResult(RoleMutationResult.Implemented(new RoleMutationResponse(roleId)));
    }
}

public sealed class PostgreSqlIamRoleApplicationService(IRoleRepository repository) : IIamRoleApplicationService
{
    public async Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var roles = await repository.ListNotDeletedAsync(cancellationToken);
        return roles
            .Where(role => string.IsNullOrWhiteSpace(options.FilterSearch)
                || role.Id.Id.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || role.RoleName.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || role.Permissions.Any(permission => permission.PermissionCode.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new RoleResponse(
                x.Id.Id,
                x.RoleName,
                x.Permissions
                    .Select(p => p.PermissionCode)
                    .OrderBy(code => code)
                    .ToArray()))
            .ApplyRoleSort(options)
            .ToPagedResponse(options);
    }

    public Task<RoleMutationResult> CreateRoleAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(RoleMutationResult.NotImplemented("Persisted role creation is not implemented."));
    }

    public Task<RoleMutationResult> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken)
    {
        return Task.FromResult(RoleMutationResult.NotImplemented("Persisted role permission updates are not implemented."));
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
