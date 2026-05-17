using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Iam.Infrastructure.Repositories;

public interface IUserRepository : IRepository<User, UserId>;

public sealed class UserRepository(ApplicationDbContext context)
    : RepositoryBase<User, UserId, ApplicationDbContext>(context), IUserRepository;

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
