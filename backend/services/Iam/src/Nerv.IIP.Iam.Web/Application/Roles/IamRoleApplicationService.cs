using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Iam.Infrastructure;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Web.Application.Roles;

public sealed record RoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record RoleMutationResponse(string RoleId);

public interface IIamRoleApplicationService
{
    Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken);

    Task<RoleMutationResponse> CreateRoleAsync(CancellationToken cancellationToken);

    Task<RoleMutationResponse> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken);
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

    public Task<RoleMutationResponse> CreateRoleAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new RoleMutationResponse("role-placeholder"));
    }

    public Task<RoleMutationResponse> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new RoleMutationResponse(roleId));
    }
}

public sealed class PostgreSqlIamRoleApplicationService(ApplicationDbContext dbContext) : IIamRoleApplicationService
{
    private static readonly Deleted NotDeleted = new(false);

    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Roles
            .AsNoTracking()
            .Where(x => x.Deleted == NotDeleted)
            .OrderBy(x => x.RoleName)
            .Select(x => new RoleResponse(
                x.Id.Id,
                x.RoleName,
                x.Permissions
                    .Select(p => p.PermissionCode)
                    .OrderBy(code => code)
                    .ToArray()))
            .ToListAsync(cancellationToken);
    }

    public Task<RoleMutationResponse> CreateRoleAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Persisted role creation is not implemented.");
    }

    public Task<RoleMutationResponse> PatchRolePermissionsAsync(string roleId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Persisted role permission updates are not implemented.");
    }
}
