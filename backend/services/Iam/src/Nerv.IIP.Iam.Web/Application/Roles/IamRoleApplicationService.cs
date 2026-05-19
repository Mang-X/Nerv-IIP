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
    Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken);

    Task<RoleMutationResult> CreateRoleAsync(CancellationToken cancellationToken);

    Task<RoleMutationResult> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken);
}

public sealed class InMemoryIamRoleApplicationService(InMemoryIamStore store) : IIamRoleApplicationService
{
    public Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RoleResponse> roles = store.Roles
            .Select(x => new RoleResponse(x.RoleId, x.RoleName, x.PermissionCodes.OrderBy(code => code).ToArray()))
            .ToArray();
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
    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await repository.ListNotDeletedAsync(cancellationToken);
        return roles
            .Select(x => new RoleResponse(
                x.Id.Id,
                x.RoleName,
                x.Permissions
                    .Select(p => p.PermissionCode)
                    .OrderBy(code => code)
                    .ToArray()))
            .ToArray();
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
