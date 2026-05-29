using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamAuthServiceScopeTests
{
    [Fact]
    public async Task GetCurrentPrincipalAsync_uses_access_token_membership_scope()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-multi-scope"),
            "multi",
            "multi@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var session = new UserSession(
            new UserSessionId("session-multi-scope"),
            user.Id,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(14),
            user.PermissionVersion,
            null,
            null);

        db.Users.Add(user);
        db.UserSessions.Add(session);
        db.Organizations.AddRange(
            new Organization(new OrganizationId("org-aaa"), "A", "active"),
            new Organization(new OrganizationId("org-zzz"), "Z", "active"));
        db.Environments.AddRange(
            new IamEnvironment(new IamEnvironmentId("env-dev"), new OrganizationId("org-aaa"), "Dev", "active"),
            new IamEnvironment(new IamEnvironmentId("env-prod"), new OrganizationId("org-zzz"), "Prod", "active"));
        db.Roles.AddRange(
            new Role(new RoleId("role-empty"), "Empty", []),
            new Role(new RoleId("role-ops"), "Ops", ["ops.tasks.create"]));
        db.Memberships.AddRange(
            new Membership(
                new MembershipId("membership-aaa"),
                user.Id,
                new OrganizationId("org-aaa"),
                new IamEnvironmentId("env-dev"),
                [new RoleId("role-empty")]),
            new Membership(
                new MembershipId("membership-zzz"),
                user.Id,
                new OrganizationId("org-zzz"),
                new IamEnvironmentId("env-prod"),
                [new RoleId("role-ops")]));
        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new ScopedMembershipRepository(user.Id),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            tokenService,
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore());

        var principalAaa = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session, "org-aaa", "env-dev")),
            CancellationToken.None);
        var principalZzz = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session, "org-zzz", "env-prod")),
            CancellationToken.None);

        Assert.NotNull(principalAaa);
        Assert.Equal("org-aaa", principalAaa.OrganizationId);
        Assert.Equal("env-dev", principalAaa.EnvironmentId);
        Assert.Empty(principalAaa.PermissionCodes);

        Assert.NotNull(principalZzz);
        Assert.Equal("org-zzz", principalZzz.OrganizationId);
        Assert.Equal("env-prod", principalZzz.EnvironmentId);
        Assert.Equal(["ops.tasks.create"], principalZzz.PermissionCodes);
    }

    [Fact]
    public async Task GetCurrentPrincipalAsync_rejects_token_scope_without_membership()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-invalid-scope"),
            "invalid-scope",
            "invalid-scope@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var session = new UserSession(
            new UserSessionId("session-invalid-scope"),
            user.Id,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(14),
            user.PermissionVersion,
            null,
            null);

        db.Users.Add(user);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var service = CreateAuthService(db, passwordService, tokenService, user.Id);
        var principal = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session, "org-forged", "env-dev")),
            CancellationToken.None);

        Assert.Null(principal);
    }

    [Fact]
    public async Task GetCurrentPrincipalAsync_without_token_scope_uses_legacy_first_membership()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-legacy-scope"),
            "legacy-scope",
            "legacy-scope@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var session = new UserSession(
            new UserSessionId("session-legacy-scope"),
            user.Id,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(14),
            user.PermissionVersion,
            null,
            null);

        db.Users.Add(user);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var service = CreateAuthService(db, passwordService, tokenService, user.Id);
        var principal = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session)),
            CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal("org-aaa", principal.OrganizationId);
        Assert.Equal("env-dev", principal.EnvironmentId);
    }

    private static ApplicationDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static IamTokenService CreateTokenService()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new IamTokenService(configuration, new DevelopmentEnvironment());
    }

    private static PostgreSqlIamAuthService CreateAuthService(
        ApplicationDbContext db,
        IamPasswordService passwordService,
        IamTokenService tokenService,
        UserId userId)
    {
        return new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new ScopedMembershipRepository(userId),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            tokenService,
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore());
    }

    private static HttpContext CreateHttpContext(string accessToken)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {accessToken}";
        return httpContext;
    }

    private sealed class DevelopmentEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Nerv.IIP.Iam.Web.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ScopedMembershipRepository(UserId userId) : IMembershipRepository
    {
        private readonly Membership firstMembership = new(
            new MembershipId("membership-first"),
            userId,
            new OrganizationId("org-aaa"),
            new IamEnvironmentId("env-dev"),
            []);
        private readonly Membership scopedMembership = new(
            new MembershipId("membership-scoped"),
            userId,
            new OrganizationId("org-zzz"),
            new IamEnvironmentId("env-prod"),
            []);

        public Task<Membership?> GetFirstByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = cancellationToken;
            return Task.FromResult<Membership?>(firstMembership);
        }

        public Task<Membership?> GetByUserIdAndOrgEnvAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = cancellationToken;
            var membership = organizationId.Id switch
            {
                "org-aaa" when environmentId.Id == "env-dev" => firstMembership,
                "org-zzz" when environmentId.Id == "env-prod" => scopedMembership,
                _ => null
            };
            return Task.FromResult<Membership?>(membership);
        }

        public Task<bool> UserHasPermissionAsync(UserId userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = permissionCode;
            _ = cancellationToken;
            return Task.FromResult(false);
        }

        public Task<bool> UserHasPermissionAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = environmentId;
            _ = cancellationToken;
            return Task.FromResult(organizationId.Id == "org-zzz" && permissionCode == "ops.tasks.create");
        }

        public Task<IReadOnlyList<string>> ListPermissionCodesAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = environmentId;
            _ = cancellationToken;
            IReadOnlyList<string> permissions = organizationId.Id == "org-zzz"
                ? ["ops.tasks.create"]
                : [];
            return Task.FromResult(permissions);
        }

        public Task<bool> UserHasMembershipAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = cancellationToken;
            return Task.FromResult(
                organizationId.Id == "org-aaa" && environmentId.Id == "env-dev"
                || organizationId.Id == "org-zzz" && environmentId.Id == "env-prod");
        }

        public IUnitOfWork UnitOfWork => throw new NotSupportedException();
        public Membership Add(Membership entity) => throw new NotSupportedException();
        public Task<Membership> AddAsync(Membership entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddRange(IEnumerable<Membership> entities) => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<Membership> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Attach(Membership entity) => throw new NotSupportedException();
        public void AttachRange(IEnumerable<Membership> entities) => throw new NotSupportedException();
        public bool Delete(Entity entity) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Entity entity) => throw new NotSupportedException();
        public int DeleteById(MembershipId id) => throw new NotSupportedException();
        public Task<int> DeleteByIdAsync(MembershipId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Membership Get(MembershipId id) => throw new NotSupportedException();
        public Task<Membership?> GetAsync(MembershipId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool Remove(Entity entity) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Entity entity) => throw new NotSupportedException();
        public Membership Update(Membership entity) => throw new NotSupportedException();
        public Task<Membership> UpdateAsync(Membership entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
