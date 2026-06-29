using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.ExternalClientAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SecurityAuditAggregate;
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
    Task PersistFailedLoginAsync(User user, CancellationToken cancellationToken = default);
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
        var normalizedLoginName = loginName.ToLowerInvariant();
        return await DbContext.Users.SingleOrDefaultAsync(
            x => x.LoginName.ToLower() == normalizedLoginName && x.Deleted == NotDeleted,
            cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.ToLowerInvariant();
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

    public async Task PersistFailedLoginAsync(User user, CancellationToken cancellationToken = default)
    {
        DbContext.Users.Update(user);
        await DbContext.SaveChangesAsync(cancellationToken);
    }
}

public interface IRoleRepository : IRepository<Role, RoleId>
{
    Task<Role?> GetByIdAsync(RoleId roleId, CancellationToken cancellationToken = default);
    Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> ListNotDeletedAsync(CancellationToken cancellationToken = default);
    Task AddAndSaveAsync(Role role, CancellationToken cancellationToken = default);
}

public sealed class DuplicateRoleNameException(string roleName)
    : Exception($"Role name '{roleName}' is already used.")
{
    public string RoleName { get; } = roleName;
}

public sealed class RoleRepository(ApplicationDbContext context)
    : RepositoryBase<Role, RoleId, ApplicationDbContext>(context), IRoleRepository
{
    private static readonly Deleted NotDeleted = new(false);

    public async Task<Role?> GetByIdAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Roles
            .Include(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == roleId && x.Deleted == NotDeleted, cancellationToken);
    }

    public async Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var normalizedRoleName = Role.NormalizeName(roleName);
        return await DbContext.Roles
            .Include(x => x.Permissions)
            .SingleOrDefaultAsync(
                x => x.NormalizedRoleName == normalizedRoleName && x.Deleted == NotDeleted,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListNotDeletedAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Roles
            .AsNoTracking()
            .Include(x => x.Permissions)
            .Where(x => x.Deleted == NotDeleted)
            .OrderBy(x => x.RoleName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAndSaveAsync(Role role, CancellationToken cancellationToken = default)
    {
        await DbContext.Roles.AddAsync(role, cancellationToken);
        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsRoleNameUniqueConstraintViolation(ex))
        {
            throw new DuplicateRoleNameException(role.RoleName);
        }
    }

    private static bool IsRoleNameUniqueConstraintViolation(DbUpdateException exception)
    {
        if (!exception.Entries.Any(entry => entry.Entity is Role))
        {
            return false;
        }

        var text = $"{exception.Message} {exception.InnerException?.Message}";
        return text.Contains("IX_roles_NormalizedRoleName", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NormalizedRoleName", StringComparison.OrdinalIgnoreCase)
            || text.Contains("23505", StringComparison.OrdinalIgnoreCase);
    }
}

public interface IMembershipRepository : IRepository<Membership, MembershipId>
{
    // Returns the stable first membership by organization and environment identifiers.
    Task<Membership?> GetFirstByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
    Task<Membership?> GetByUserIdAndOrgEnvAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        CancellationToken cancellationToken = default);
    Task<bool> UserHasPermissionAsync(UserId userId, string permissionCode, CancellationToken cancellationToken = default);
    Task<bool> UserHasPermissionAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string permissionCode,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPermissionCodesAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        CancellationToken cancellationToken = default);
    Task<bool> UserHasMembershipAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
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
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.EnvironmentId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Membership?> GetByUserIdAndOrgEnvAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.Memberships.SingleOrDefaultAsync(
            x => x.UserId == userId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId,
            cancellationToken);
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

    public async Task<IReadOnlyList<string>> ListPermissionCodesAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
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
            select rolePermission.PermissionCode)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UserHasMembershipAsync(
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.Memberships.AnyAsync(
            x => x.UserId == userId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId,
            cancellationToken);
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
    Task<UserSession?> GetByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken = default);
    Task<UserSession?> ConsumeActiveRefreshTokenAsync(
        string refreshTokenHash,
        DateTimeOffset now,
        string revokedReason,
        CancellationToken cancellationToken = default);
    Task<int> RevokeFamilyAsync(
        string tokenFamilyId,
        DateTimeOffset now,
        string revokedReason,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSession>> ListActiveByUserIdAsync(
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSession>> ListActiveByExternalIdentityAsync(
        string externalProvider,
        string externalSubject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
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

    public async Task<UserSession?> GetByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .SingleOrDefaultAsync(x => x.RefreshTokenHash == refreshTokenHash, cancellationToken);
    }

    public async Task<UserSession?> ConsumeActiveRefreshTokenAsync(
        string refreshTokenHash,
        DateTimeOffset now,
        string revokedReason,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = DbContext.Database.CurrentTransaction is null
            ? await DbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var affectedRows = await DbContext.UserSessions
            .Where(x => x.RefreshTokenHash == refreshTokenHash && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, revokedReason),
                cancellationToken);

        if (affectedRows != 1)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return null;
        }

        var session = await DbContext.UserSessions.SingleAsync(x => x.RefreshTokenHash == refreshTokenHash, cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return session;
    }

    public async Task<int> RevokeFamilyAsync(
        string tokenFamilyId,
        DateTimeOffset now,
        string revokedReason,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .Where(x => x.TokenFamilyId == tokenFamilyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, revokedReason),
                cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .AsNoTracking()
            .OrderByDescending(x => x.IssuedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> ListActiveByUserIdAsync(
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.IssuedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> ListActiveByExternalIdentityAsync(
        string externalProvider,
        string externalSubject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UserSessions
            .Where(x => x.ExternalProvider == externalProvider
                && x.ExternalSubject == externalSubject
                && x.RevokedAtUtc == null
                && x.ExpiresAtUtc > now)
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

public interface IExternalClientRepository : IRepository<ExternalClient, ExternalClientId>
{
    Task<ExternalClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActiveGrantPermissionCodesAsync(
        string clientId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<bool> HasActiveGrantAsync(
        string clientId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string permissionCode,
        string? resourceType,
        string? resourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalClientRepository(ApplicationDbContext context)
    : RepositoryBase<ExternalClient, ExternalClientId, ApplicationDbContext>(context), IExternalClientRepository
{
    public async Task<ExternalClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return await DbContext.ExternalClients.SingleOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActiveGrantPermissionCodesAsync(
        string clientId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await ActiveGrantQuery(clientId, organizationId, environmentId, now)
            .Select(x => x.PermissionCode)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasActiveGrantAsync(
        string clientId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string permissionCode,
        string? resourceType,
        string? resourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var normalizedResourceType = AuthorizationGrant.NormalizeResourceScope(resourceType);
        var normalizedResourceId = AuthorizationGrant.NormalizeResourceScope(resourceId);
        return await ActiveGrantQuery(clientId, organizationId, environmentId, now)
            .AnyAsync(
                x => x.PermissionCode == permissionCode
                    && (x.ResourceType == "*"
                        || (x.ResourceType == normalizedResourceType
                            && (x.ResourceId == "*" || x.ResourceId == normalizedResourceId))),
                cancellationToken);
    }

    private IQueryable<AuthorizationGrant> ActiveGrantQuery(
        string clientId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        DateTimeOffset now)
    {
        return DbContext.AuthorizationGrants.Where(x =>
            x.PrincipalType == "external-client"
            && x.PrincipalId == clientId
            && x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.ValidFromUtc <= now
            && (x.ValidToUtc == null || x.ValidToUtc > now)
            && x.RevokedAtUtc == null);
    }
}

public interface ISeedManifestRepository : IRepository<SeedManifest, SeedManifestId>;

public sealed class SeedManifestRepository(ApplicationDbContext context)
    : RepositoryBase<SeedManifest, SeedManifestId, ApplicationDbContext>(context), ISeedManifestRepository;

public interface ISecurityAuditRepository : IRepository<SecurityAuditRecord, SecurityAuditRecordId>
{
    Task<IReadOnlyList<SecurityAuditRecord>> ListAsync(
        string? organizationId,
        string? environmentId,
        string? action,
        string? targetType,
        string? targetId,
        DateTimeOffset? occurredFromUtc,
        DateTimeOffset? occurredToUtc,
        int take,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class SecurityAuditRepository(ApplicationDbContext context)
    : RepositoryBase<SecurityAuditRecord, SecurityAuditRecordId, ApplicationDbContext>(context), ISecurityAuditRepository
{
    public async Task<IReadOnlyList<SecurityAuditRecord>> ListAsync(
        string? organizationId,
        string? environmentId,
        string? action,
        string? targetType,
        string? targetId,
        DateTimeOffset? occurredFromUtc,
        DateTimeOffset? occurredToUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = DbContext.SecurityAuditRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            query = query.Where(x => x.OrganizationId == organizationId);
        }

        if (!string.IsNullOrWhiteSpace(environmentId))
        {
            query = query.Where(x => x.EnvironmentId == environmentId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            query = query.Where(x => x.TargetType == targetType);
        }

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            query = query.Where(x => x.TargetId == targetId);
        }

        if (occurredFromUtc is not null)
        {
            query = query.Where(x => x.OccurredAtUtc >= occurredFromUtc);
        }

        if (occurredToUtc is not null)
        {
            query = query.Where(x => x.OccurredAtUtc <= occurredToUtc);
        }

        return await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
