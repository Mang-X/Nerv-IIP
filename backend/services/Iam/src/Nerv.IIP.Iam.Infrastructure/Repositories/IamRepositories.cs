using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Iam.Infrastructure.Repositories;

public interface IUserRepository : IRepository<User, UserId>
{
    Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default);
    Task<User?> GetByLoginNameAsync(string loginName, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> ListNotDeletedAsync(CancellationToken cancellationToken = default);
}

public sealed class UserRepository(ApplicationDbContext context)
    : RepositoryBase<User, UserId, ApplicationDbContext>(context), IUserRepository
{
    private static readonly Deleted NotDeleted = new(false);

    public async Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Deleted == NotDeleted, cancellationToken);
    }

    public async Task<User?> GetByLoginNameAsync(string loginName, CancellationToken cancellationToken = default)
    {
        var normalizedLoginName = loginName.ToLower();
        return await DbContext.Users.SingleOrDefaultAsync(
            x => x.LoginName.ToLower() == normalizedLoginName && x.Deleted == NotDeleted,
            cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.ToLower();
        return await DbContext.Users.SingleOrDefaultAsync(
            x => x.Email.ToLower() == normalizedEmail && x.Deleted == NotDeleted,
            cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListNotDeletedAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Users
            .AsNoTracking()
            .Where(x => x.Deleted == NotDeleted)
            .OrderBy(x => x.LoginName)
            .ToListAsync(cancellationToken);
    }
}

public interface IRoleRepository : IRepository<Role, RoleId>
{
    Task<IReadOnlyList<Role>> ListNotDeletedAsync(CancellationToken cancellationToken = default);
}

public sealed class RoleRepository(ApplicationDbContext context)
    : RepositoryBase<Role, RoleId, ApplicationDbContext>(context), IRoleRepository
{
    private static readonly Deleted NotDeleted = new(false);

    public async Task<IReadOnlyList<Role>> ListNotDeletedAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Roles
            .AsNoTracking()
            .Include(x => x.Permissions)
            .Where(x => x.Deleted == NotDeleted)
            .OrderBy(x => x.RoleName)
            .ToListAsync(cancellationToken);
    }
}

public interface IMembershipRepository : IRepository<Membership, MembershipId>
{
    Task<Membership?> GetFirstByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
    Task<bool> UserHasPermissionAsync(UserId userId, string permissionCode, CancellationToken cancellationToken = default);
    Task<bool> UserHasPermissionAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string permissionCode,
        CancellationToken cancellationToken = default);
}

public sealed class MembershipRepository(ApplicationDbContext context)
    : RepositoryBase<Membership, MembershipId, ApplicationDbContext>(context), IMembershipRepository
{
    private static readonly Deleted NotDeleted = new(false);

    public async Task<Membership?> GetFirstByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Memberships
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UserHasPermissionAsync(
        UserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        return await (
            from membership in DbContext.Memberships
            join membershipRole in DbContext.MembershipRoles on membership.Id equals membershipRole.MembershipId
            join role in DbContext.Roles on membershipRole.RoleId equals role.Id
            join rolePermission in DbContext.RolePermissions on role.Id equals rolePermission.RoleId
            where membership.UserId == userId
                && role.Deleted == NotDeleted
                && rolePermission.PermissionCode == permissionCode
            select rolePermission.Id)
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> UserHasPermissionAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        return await (
            from membership in DbContext.Memberships
            join membershipRole in DbContext.MembershipRoles on membership.Id equals membershipRole.MembershipId
            join role in DbContext.Roles on membershipRole.RoleId equals role.Id
            join rolePermission in DbContext.RolePermissions on role.Id equals rolePermission.RoleId
            where membership.UserId == userId
                && membership.OrganizationId == organizationId
                && membership.EnvironmentId == environmentId
                && role.Deleted == NotDeleted
                && rolePermission.PermissionCode == permissionCode
            select rolePermission.Id)
            .AnyAsync(cancellationToken);
    }
}

public interface IUserSessionRepository : IRepository<UserSession, UserSessionId>
{
    Task<UserSession?> GetByIdAsync(UserSessionId sessionId, CancellationToken cancellationToken = default);
    Task<UserSession?> GetByPrincipalAsync(
        UserSessionId sessionId,
        UserId userId,
        CancellationToken cancellationToken = default);
    Task<UserSession?> GetActiveByRefreshTokenHashAsync(
        string refreshTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class UserSessionRepository(ApplicationDbContext context)
    : RepositoryBase<UserSession, UserSessionId, ApplicationDbContext>(context), IUserSessionRepository
{
    public async Task<UserSession?> GetByIdAsync(UserSessionId sessionId, CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions.FindAsync([sessionId], cancellationToken);
    }

    public async Task<UserSession?> GetByPrincipalAsync(
        UserSessionId sessionId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);
    }

    public async Task<UserSession?> GetActiveByRefreshTokenHashAsync(
        string refreshTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .SingleOrDefaultAsync(
                x => x.RefreshTokenHash == refreshTokenHash && x.RevokedAtUtc == null && x.ExpiresAtUtc > now,
                cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .AsNoTracking()
            .OrderByDescending(x => x.IssuedAtUtc)
            .ToListAsync(cancellationToken);
    }
}

public interface IConnectorHostCredentialRepository : IRepository<ConnectorHostCredential, ConnectorHostCredentialId>
{
    Task<ConnectorHostCredential?> GetByConnectorHostAndSecretHashAsync(
        string connectorHostId,
        string secretHash,
        CancellationToken cancellationToken = default);
}

public sealed class ConnectorHostCredentialRepository(ApplicationDbContext context)
    : RepositoryBase<ConnectorHostCredential, ConnectorHostCredentialId, ApplicationDbContext>(context), IConnectorHostCredentialRepository
{
    public async Task<ConnectorHostCredential?> GetByConnectorHostAndSecretHashAsync(
        string connectorHostId,
        string secretHash,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.ConnectorHostCredentials
            .SingleOrDefaultAsync(x => x.ConnectorHostId == connectorHostId && x.SecretHash == secretHash, cancellationToken);
    }
}

public interface ISeedManifestRepository : IRepository<SeedManifest, SeedManifestId>;

public sealed class SeedManifestRepository(ApplicationDbContext context)
    : RepositoryBase<SeedManifest, SeedManifestId, ApplicationDbContext>(context), ISeedManifestRepository;
