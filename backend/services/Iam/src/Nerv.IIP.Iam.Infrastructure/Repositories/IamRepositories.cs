using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
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

public interface IRoleRepository : IRepository<Role, RoleId>;

public sealed class RoleRepository(ApplicationDbContext context)
    : RepositoryBase<Role, RoleId, ApplicationDbContext>(context), IRoleRepository;

public interface IMembershipRepository : IRepository<Membership, MembershipId>;

public sealed class MembershipRepository(ApplicationDbContext context)
    : RepositoryBase<Membership, MembershipId, ApplicationDbContext>(context), IMembershipRepository;

public interface IUserSessionRepository : IRepository<UserSession, UserSessionId>;

public sealed class UserSessionRepository(ApplicationDbContext context)
    : RepositoryBase<UserSession, UserSessionId, ApplicationDbContext>(context), IUserSessionRepository;

public interface IConnectorHostCredentialRepository : IRepository<ConnectorHostCredential, ConnectorHostCredentialId>;

public sealed class ConnectorHostCredentialRepository(ApplicationDbContext context)
    : RepositoryBase<ConnectorHostCredential, ConnectorHostCredentialId, ApplicationDbContext>(context), IConnectorHostCredentialRepository;

public interface ISeedManifestRepository : IRepository<SeedManifest, SeedManifestId>;

public sealed class SeedManifestRepository(ApplicationDbContext context)
    : RepositoryBase<SeedManifest, SeedManifestId, ApplicationDbContext>(context), ISeedManifestRepository;
