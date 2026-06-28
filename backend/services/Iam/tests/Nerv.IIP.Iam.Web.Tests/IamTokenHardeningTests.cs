using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.ExternalClientAggregate;
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

public sealed class IamTokenHardeningTests
{
    [Fact]
    public void Machine_secret_hashes_are_versioned_hmac_values_and_verify_without_plain_sha256_storage()
    {
        var tokenService = CreateTokenService("test-pepper");
        const string secret = "machine-secret-value";

        var hash = tokenService.HashSecret(secret);
        var bareSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

        Assert.StartsWith("hmac-sha256:v1:", hash, StringComparison.Ordinal);
        Assert.NotEqual(bareSha256, hash);
        Assert.True(tokenService.VerifySecret(secret, hash));
        Assert.False(tokenService.VerifySecret("wrong-machine-secret-value", hash));
    }

    [Fact]
    public async Task Client_credentials_authenticates_hmac_stored_secret_and_rejects_bare_sha256_storage()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateSqliteDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var tokenService = CreateTokenService("test-pepper");
        var passwordService = new IamPasswordService();
        const string secret = "external-client-secret";
        var now = DateTimeOffset.UtcNow;
        var hmacClient = new ExternalClient(
            new ExternalClientId("external-client-hmac"),
            "external-client-hmac",
            "HMAC Client",
            new OrganizationId("org-machine"),
            new IamEnvironmentId("env-machine"),
            tokenService.HashSecret(secret),
            true,
            1,
            now.AddMinutes(-5),
            null);
        var shaClient = new ExternalClient(
            new ExternalClientId("external-client-sha"),
            "external-client-sha",
            "SHA Client",
            new OrganizationId("org-machine"),
            new IamEnvironmentId("env-machine"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant(),
            true,
            1,
            now.AddMinutes(-5),
            null);
        var auth = CreateAuthService(
            db,
            new FakeUserSessionRepository(),
            passwordService,
            tokenService,
            new FakeExternalClientRepository(hmacClient, shaClient));

        var issued = await auth.IssueClientCredentialsTokenAsync(
            "external-client-hmac",
            secret,
            "ops.tasks.create",
            CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.IssueClientCredentialsTokenAsync(
                "external-client-sha",
                secret,
                "ops.tasks.create",
                CancellationToken.None));

        Assert.Equal("Bearer", issued.TokenType);
        Assert.Equal("ops.tasks.create", issued.Scope);
    }

    [Fact]
    public async Task Connector_credentials_authenticate_hmac_stored_secret_and_reject_bare_sha256_storage()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateSqliteDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var tokenService = CreateTokenService("test-pepper");
        var passwordService = new IamPasswordService();
        var auth = CreateAuthService(db, new FakeUserSessionRepository(), passwordService, tokenService);
        const string secret = "connector-secret";
        var now = DateTimeOffset.UtcNow;

        db.Organizations.Add(new Organization(new OrganizationId("org-connector"), "Connector", "active"));
        db.Environments.Add(new IamEnvironment(new IamEnvironmentId("env-connector"), new OrganizationId("org-connector"), "Connector", "active"));
        db.ConnectorHostCredentials.Add(new ConnectorHostCredential(
            new ConnectorHostCredentialId("connector-hmac-credential"),
            "connector-host-hmac",
            new OrganizationId("org-connector"),
            new IamEnvironmentId("env-connector"),
            tokenService.HashSecret(secret),
            now.AddMinutes(-5),
            null,
            ["connectors.registrations.write"]));
        db.ConnectorHostCredentials.Add(new ConnectorHostCredential(
            new ConnectorHostCredentialId("connector-sha-credential"),
            "connector-host-sha",
            new OrganizationId("org-connector"),
            new IamEnvironmentId("env-connector"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant(),
            now.AddMinutes(-5),
            null,
            ["connectors.registrations.write"]));
        await db.SaveChangesAsync();

        var principal = await auth.ValidateConnectorCredentialAsync(
            "connector-host-hmac",
            secret,
            CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.ValidateConnectorCredentialAsync(
                "connector-host-sha",
                secret,
                CancellationToken.None));

        Assert.Equal("connector-host", principal.PrincipalType);
        Assert.Equal("org-connector", principal.OrganizationId);
        Assert.Equal("env-connector", principal.EnvironmentId);
    }

    [Fact]
    public async Task Refresh_replay_revokes_latest_session_in_same_token_family()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateSqliteDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = await SeedUserWithMembershipAsync(db, passwordService);
        var tokenService = CreateTokenService();
        var sessions = new FakeUserSessionRepository();
        var auth = CreateAuthService(db, sessions, passwordService, tokenService);

        var login = await auth.LoginAsync(user.LoginName, "Password123!", "test-client", "127.0.0.1", CancellationToken.None);
        var rotated = await auth.RefreshAsync(login.RefreshToken, "test-client", "127.0.0.1", CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            auth.RefreshAsync(login.RefreshToken, "attacker", "10.0.0.10", CancellationToken.None));

        var principalAfterReplay = await auth.GetCurrentPrincipalAsync(CreateHttpContext(rotated.AccessToken), CancellationToken.None);
        Assert.Null(principalAfterReplay);

        Assert.All(sessions.Sessions, session => Assert.NotNull(session.RevokedAtUtc));
        Assert.Contains(sessions.Sessions, session => session.Id.Id == rotated.SessionId && session.RevokedReason == "refresh-reuse-detected");
    }

    [Fact]
    public async Task Jwks_endpoint_exposes_public_rs256_key_that_validates_issued_access_token()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new LoginRequest("admin", "Admin123!"));
        login.EnsureSuccessStatusCode();
        var tokens = await ReadResponseDataAsync<AuthResponse>(login);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);

        Assert.Equal(SecurityAlgorithms.RsaSha256, jwt.Header.Alg);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Header.Kid));

        var jwksResponse = await client.GetAsync("/api/iam/v1/auth/jwks");
        Assert.Equal(HttpStatusCode.OK, jwksResponse.StatusCode);
        var jwksJson = await jwksResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"d\"", jwksJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", jwksJson, StringComparison.OrdinalIgnoreCase);

        var keySet = new JsonWebKeySet(jwksJson);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "nerv-iip-iam",
            ValidateAudience = true,
            ValidAudience = "nerv-iip-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keySet.Keys,
            ValidateLifetime = true
        };

        new JwtSecurityTokenHandler().ValidateToken(tokens.AccessToken, parameters, out var validatedToken);
        var validatedJwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(jwt.Header.Kid, validatedJwt.Header.Kid);
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static IamTokenService CreateTokenService(string? pepper = null)
    {
        var values = pepper is null
            ? []
            : new Dictionary<string, string?> { ["Iam:Secrets:Pepper"] = pepper };
        return new IamTokenService(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new TestWebHostEnvironment());
    }

    private static PostgreSqlIamAuthService CreateAuthService(
        ApplicationDbContext db,
        IUserSessionRepository userSessionRepository,
        IamPasswordService passwordService,
        IamTokenService tokenService,
        IExternalClientRepository? externalClientRepository = null)
    {
        return new PostgreSqlIamAuthService(
            new UserRepository(db),
            userSessionRepository,
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            externalClientRepository ?? new ExternalClientRepository(db),
            passwordService,
            tokenService,
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
    }

    private sealed class FakeExternalClientRepository(params ExternalClient[] clients) : IExternalClientRepository
    {
        public Task<ExternalClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(clients.SingleOrDefault(x => x.ClientId == clientId));
        }

        public Task<IReadOnlyList<string>> ListActiveGrantPermissionCodesAsync(
            string clientId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            _ = organizationId;
            _ = environmentId;
            _ = now;
            _ = cancellationToken;
            IReadOnlyList<string> permissions = clientId == "external-client-hmac"
                ? ["ops.tasks.create"]
                : [];
            return Task.FromResult(permissions);
        }

        public Task<bool> HasActiveGrantAsync(
            string clientId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            string permissionCode,
            string? resourceType,
            string? resourceId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IUnitOfWork UnitOfWork => throw new NotSupportedException();
        public ExternalClient Add(ExternalClient entity) => throw new NotSupportedException();
        public Task<ExternalClient> AddAsync(ExternalClient entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddRange(IEnumerable<ExternalClient> entities) => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<ExternalClient> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Attach(ExternalClient entity) => throw new NotSupportedException();
        public void AttachRange(IEnumerable<ExternalClient> entities) => throw new NotSupportedException();
        public bool Delete(Entity entity) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Entity entity) => throw new NotSupportedException();
        public int DeleteById(ExternalClientId id) => throw new NotSupportedException();
        public Task<int> DeleteByIdAsync(ExternalClientId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ExternalClient Get(ExternalClientId id) => throw new NotSupportedException();
        public Task<ExternalClient?> GetAsync(ExternalClientId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool Remove(Entity entity) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Entity entity) => throw new NotSupportedException();
        public ExternalClient Update(ExternalClient entity) => throw new NotSupportedException();
        public Task<ExternalClient> UpdateAsync(ExternalClient entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<User> SeedUserWithMembershipAsync(ApplicationDbContext db, IamPasswordService passwordService)
    {
        var user = new User(
            new UserId("user-refresh-replay"),
            "refresh-replay",
            "refresh-replay@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Default", "active"));
        db.Environments.Add(new IamEnvironment(new IamEnvironmentId("env-dev"), new OrganizationId("org-001"), "Dev", "active"));
        db.Roles.Add(new Role(new RoleId("role-admin"), "Admin", ["apphub.instances.read"]));
        db.Memberships.Add(new Membership(
            new MembershipId("membership-refresh-replay"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [new RoleId("role-admin")]));
        await db.SaveChangesAsync();
        return user;
    }

    private static HttpContext CreateHttpContext(string accessToken)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {accessToken}";
        return httpContext;
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }

    private sealed class FakeUserSessionRepository : IUserSessionRepository
    {
        private readonly List<UserSession> sessions = [];

        public IReadOnlyList<UserSession> Sessions => sessions;

        public Task<UserSession?> GetByIdAsync(UserSessionId sessionId, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(sessions.SingleOrDefault(x => x.Id == sessionId));
        }

        public Task<UserSession?> GetByPrincipalAsync(
            UserSessionId sessionId,
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(sessions.SingleOrDefault(x => x.Id == sessionId && x.UserId == userId));
        }

        public Task<UserSession?> GetActiveByRefreshTokenHashAsync(
            string refreshTokenHash,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(sessions.SingleOrDefault(x => x.RefreshTokenHash == refreshTokenHash && x.CanRefresh(now)));
        }

        public Task<UserSession?> GetByRefreshTokenHashAsync(
            string refreshTokenHash,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(sessions.SingleOrDefault(x => x.RefreshTokenHash == refreshTokenHash));
        }

        public Task<UserSession?> ConsumeActiveRefreshTokenAsync(
            string refreshTokenHash,
            DateTimeOffset now,
            string revokedReason,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var session = sessions.SingleOrDefault(x => x.RefreshTokenHash == refreshTokenHash && x.CanRefresh(now));
            session?.Revoke(now, revokedReason);
            return Task.FromResult(session);
        }

        public Task<int> RevokeFamilyAsync(
            string tokenFamilyId,
            DateTimeOffset now,
            string revokedReason,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var count = 0;
            foreach (var session in sessions.Where(x => x.TokenFamilyId == tokenFamilyId && x.RevokedAtUtc is null))
            {
                session.Revoke(now, revokedReason);
                count++;
            }

            return Task.FromResult(count);
        }

        public Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<UserSession>>(sessions.OrderByDescending(x => x.IssuedAtUtc).ToList());
        }

        public Task<IReadOnlyList<UserSession>> ListActiveByUserIdAsync(
            UserId userId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<UserSession>>(
                sessions.Where(x => x.UserId == userId && x.CanRefresh(now)).OrderByDescending(x => x.IssuedAtUtc).ToList());
        }

        public Task<IReadOnlyList<UserSession>> ListActiveByExternalIdentityAsync(
            string externalProvider,
            string externalSubject,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<UserSession>>(
                sessions
                    .Where(x => x.ExternalProvider == externalProvider
                        && x.ExternalSubject == externalSubject
                        && x.CanRefresh(now))
                    .OrderByDescending(x => x.IssuedAtUtc)
                    .ToList());
        }

        public Task<UserSession> AddAsync(UserSession entity, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            sessions.Add(entity);
            return Task.FromResult(entity);
        }

        public IUnitOfWork UnitOfWork => throw new NotSupportedException();
        public UserSession Add(UserSession entity) => throw new NotSupportedException();
        public void AddRange(IEnumerable<UserSession> entities) => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<UserSession> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Attach(UserSession entity) => throw new NotSupportedException();
        public void AttachRange(IEnumerable<UserSession> entities) => throw new NotSupportedException();
        public bool Delete(Entity entity) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Entity entity) => throw new NotSupportedException();
        public int DeleteById(UserSessionId id) => throw new NotSupportedException();
        public Task<int> DeleteByIdAsync(UserSessionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public UserSession Get(UserSessionId id) => throw new NotSupportedException();
        public Task<UserSession?> GetAsync(UserSessionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool Remove(Entity entity) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Entity entity) => throw new NotSupportedException();
        public UserSession Update(UserSession entity) => throw new NotSupportedException();
        public Task<UserSession> UpdateAsync(UserSession entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
