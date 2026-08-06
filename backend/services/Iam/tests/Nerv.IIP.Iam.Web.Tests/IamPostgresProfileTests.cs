using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.DataScopes;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Testing;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamPostgresProfileTests
{
    [Fact]
    public async Task Fresh_Postgres_has_case_insensitive_user_unique_indexes()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                postgresConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam"))
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var loginIndexIsUniqueLowerExpression = await IsUniqueLowerExpressionIndexAsync(
            db,
            "IX_users_LoginName_Lower",
            "LoginName");
        var emailIndexIsUniqueLowerExpression = await IsUniqueLowerExpressionIndexAsync(
            db,
            "IX_users_Email_Lower",
            "Email");

        Assert.True(
            loginIndexIsUniqueLowerExpression,
            "Expected unique lower-expression index 'IX_users_LoginName_Lower' on iam.users.LoginName.");
        Assert.True(
            emailIndexIsUniqueLowerExpression,
            "Expected unique lower-expression index 'IX_users_Email_Lower' on iam.users.Email.");

        db.Users.Add(new User(
            new UserId("user-admin-upper"),
            "Admin",
            "Admin@nerv-iip.local",
            "password-hash",
            true,
            "security-stamp-upper",
            1));
        await db.SaveChangesAsync();

        var loginCaseDuplicate = new User(
            new UserId("user-admin-lower"),
            "admin",
            "different-email@nerv-iip.local",
            "password-hash",
            true,
            "security-stamp-lower",
            1);
        db.Users.Add(loginCaseDuplicate);
        var loginException = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var loginPostgresException = Assert.IsType<PostgresException>(loginException.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, loginPostgresException.SqlState);
        Assert.Equal("IX_users_LoginName_Lower", loginPostgresException.ConstraintName);
        db.Entry(loginCaseDuplicate).State = EntityState.Detached;

        db.Users.Add(new User(
            new UserId("user-email-lower"),
            "different-login",
            "admin@nerv-iip.local",
            "password-hash",
            true,
            "security-stamp-email-lower",
            1));
        var emailException = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var emailPostgresException = Assert.IsType<PostgresException>(emailException.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, emailPostgresException.SqlState);
        Assert.Equal("IX_users_Email_Lower", emailPostgresException.ConstraintName);
    }

    [Fact]
    public async Task Postgres_profile_seeds_admin_and_persists_login_refresh_replay_logout_and_connector_validation()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
            await seed.SeedAsync(CancellationToken.None);

            var historyTableExists = await db.Database.SqlQueryRaw<int>(
                """
                select count(*)::int as "Value"
                from information_schema.tables
                where table_schema = 'iam'
                  and table_name = '__EFMigrationsHistory'
                """).SingleAsync();

            Assert.Equal(1, historyTableExists);
        }

        var client = factory.CreateClient();

        var failedLogin = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var admin = await db.Users.SingleAsync(x => x.LoginName == "admin");
            Assert.Equal(1, admin.FailedLoginCount);
        }

        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);

        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.SessionId));

        var anonymousSessions = await client.GetAsync("/api/iam/v1/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousSessions.StatusCode);

        var anonymousRevoke = await client.PostAsync($"/api/iam/v1/sessions/{auth.SessionId}/revoke", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRevoke.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var sessions = await client.GetAsync("/api/iam/v1/sessions");
        sessions.EnsureSuccessStatusCode();

        var me = await client.GetAsync("/api/iam/v1/me");
        me.EnsureSuccessStatusCode();
        var principal = await ReadResponseDataAsync<MeResponse>(me);
        Assert.Equal("user-admin", principal.UserId);
        Assert.Equal("admin", principal.LoginName);
        Assert.Equal("user", principal.PrincipalType);
        Assert.Equal("org-001", principal.OrganizationId);
        Assert.Equal("env-dev", principal.EnvironmentId);
        Assert.Equal(1, principal.PermissionVersion);
        Assert.True(auth.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var refresh = await client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await ReadResponseDataAsync<AuthResponse>(refresh);
        Assert.False(string.IsNullOrWhiteSpace(rotated.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(rotated.SessionId));
        Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);

        var oldRefresh = await client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        var meWithRotatedAccessToken = await client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meWithRotatedAccessToken.StatusCode);

        var logout = await client.PostAsJsonAsync("/api/iam/v1/auth/logout", new { sessionId = rotated.SessionId });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var meAfterLogout = await client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var secondLogin = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        secondLogin.EnsureSuccessStatusCode();
        var secondAuth = await ReadResponseDataAsync<AuthResponse>(secondLogin);

        client.DefaultRequestHeaders.Authorization = new("Bearer", secondAuth.AccessToken);
        var adminRevoke = await client.PostAsync($"/api/iam/v1/sessions/{secondAuth.SessionId}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, adminRevoke.StatusCode);

        var meAfterAdminRevoke = await client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterAdminRevoke.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;

        var connector = await client.PostAsJsonAsync(
            "/api/iam/v1/connectors/credentials/validate",
            new { connectorHostId = "connector-host-001", secret = "local-connector-secret" });
        connector.EnsureSuccessStatusCode();
        var connectorPrincipal = await ReadResponseDataAsync<ConnectorPrincipalResponse>(connector);
        Assert.Equal("connector-host", connectorPrincipal.PrincipalType);
        Assert.Equal("connector-host-001", connectorPrincipal.ConnectorHostId);
        Assert.Equal("org-001", connectorPrincipal.OrganizationId);
        Assert.Equal("env-dev", connectorPrincipal.EnvironmentId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var adminUsers = await db.Users.Where(x => x.LoginName == "admin").ToListAsync();
            var admin = Assert.Single(adminUsers);
            Assert.DoesNotContain("Admin123!", admin.PasswordHash, StringComparison.Ordinal);
            Assert.Equal(0, admin.FailedLoginCount);

            var connectorCredentials = await db.ConnectorHostCredentials
                .Where(x => x.ConnectorHostId == "connector-host-001")
                .ToListAsync();
            Assert.Single(connectorCredentials);
        }
    }

    [Fact]
    public async Task Postgres_refresh_token_rotation_consumes_token_once_and_replay_revokes_token_family()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);
        Assert.NotNull(auth);

        var refreshRequests = Enumerable
            .Range(0, 12)
            .Select(_ => client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { auth.RefreshToken }))
            .ToArray();

        var refreshResponses = await Task.WhenAll(refreshRequests);

        Assert.Equal(1, refreshResponses.Count(response => response.IsSuccessStatusCode));
        Assert.Equal(11, refreshResponses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var adminUserId = new UserId("user-admin");
            var sessionCount = await db.UserSessions.CountAsync(x => x.UserId == adminUserId);
            var activeSessionCount = await db.UserSessions.CountAsync(x => x.UserId == adminUserId && x.RevokedAtUtc == null);

            Assert.Equal(2, sessionCount);
            Assert.Equal(0, activeSessionCount);
        }
    }

    [Fact]
    public async Task Postgres_refresh_replay_after_consumption_commit_revokes_rotated_session()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        var interceptor = new PauseAfterArmedTransactionCommitInterceptor();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddDbContext<ApplicationDbContext>(
                        (_, options) => options.AddInterceptors(interceptor));
                });
            });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);

        await using var rotatingScope = factory.Services.CreateAsyncScope();
        await using var replayScope = factory.Services.CreateAsyncScope();
        var rotatingAuth = rotatingScope.ServiceProvider.GetRequiredService<IIamAuthService>();
        var rotatingSessions = rotatingScope.ServiceProvider.GetRequiredService<IUserSessionRepository>();
        var replayAuth = replayScope.ServiceProvider.GetRequiredService<IIamAuthService>();

        interceptor.Arm();
        var rotationTask = RotateAndSaveAsync();
        await interceptor.WaitUntilCommitPausedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => replayAuth.RefreshAsync(
                    auth.RefreshToken,
                    "replay-client",
                    "10.0.0.10",
                    CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            interceptor.Release();
        }

        var rotated = await rotationTask.WaitAsync(TimeSpan.FromSeconds(10));

        client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        var me = await client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var adminUserId = new UserId("user-admin");
            var sessions = await db.UserSessions
                .Where(x => x.UserId == adminUserId)
                .ToListAsync();

            Assert.Equal(2, sessions.Count);
            Assert.All(sessions, session => Assert.NotNull(session.RevokedAtUtc));
            Assert.Contains(sessions, session => session.Id.Id == rotated.SessionId
                && session.RevokedReason == "refresh-reuse-detected");
        }

        async Task<Nerv.IIP.Iam.Web.Application.Auth.AuthResponse> RotateAndSaveAsync()
        {
            var response = await rotatingAuth.RefreshAsync(
                auth.RefreshToken,
                "rotating-client",
                "127.0.0.1",
                CancellationToken.None);
            await rotatingSessions.UnitOfWork.SaveEntitiesAsync();
            return response;
        }
    }

    [Fact]
    public async Task Postgres_refresh_token_rotation_rolls_back_when_rotated_session_insert_fails()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        var interceptor = new FailNextSessionInsertInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                postgresConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam"))
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var session = new UserSession(
            new UserSessionId("session-rollback-proof"),
            new UserId("user-admin"),
            "refresh-token-hash",
            now,
            now.AddDays(14),
            1,
            null,
            null);
        await db.UserSessions.AddAsync(session);
        await db.SaveChangesAsync();

        var rotatedSession = new UserSession(
            new UserSessionId("session-rollback-proof-rotated"),
            new UserId("user-admin"),
            "refresh-token-hash-rotated",
            now,
            now.AddDays(14),
            1,
            null,
            null,
            tokenFamilyId: session.TokenFamilyId,
            previousSessionId: session.Id.Id);
        interceptor.FailNextSessionInsert = true;
        var repository = new UserSessionRepository(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RotateActiveRefreshTokenAsync(
            "refresh-token-hash",
            now,
            "refresh-rotated",
            rotatedSession,
            CancellationToken.None));
        var updateException = Assert.IsType<DbUpdateException>(exception.InnerException);
        Assert.IsType<TimeoutException>(updateException.InnerException);

        db.ChangeTracker.Clear();
        var originalSession = await db.UserSessions.SingleAsync();
        Assert.Equal(new UserSessionId("session-rollback-proof"), originalSession.Id);
        Assert.Null(originalSession.RevokedAtUtc);
    }

    [Fact]
    public async Task Postgres_login_lockout_blocks_attempts_and_success_resets_failed_state()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var successfulLogin = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        successfulLogin.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var admin = await db.Users.SingleAsync(x => x.LoginName == "admin");

            Assert.Equal(0, admin.FailedLoginCount);
            Assert.Null(admin.LockoutUntilUtc);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var admin = await db.Users.SingleAsync(x => x.LoginName == "admin");

            Assert.Equal(5, admin.FailedLoginCount);
            Assert.True(admin.LockoutUntilUtc > DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task Postgres_profile_persists_user_create_update_and_disable_commands()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);

        var create = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName = "operator", email = "operator@nerv-iip.local", password = "Operator123!" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadResponseDataAsync<UserResponse>(create);

        Assert.False(string.IsNullOrWhiteSpace(created.UserId));
        Assert.Equal("operator", created.LoginName);
        Assert.Equal("operator@nerv-iip.local", created.Email);
        Assert.True(created.Enabled);

        var patch = await client.PatchAsJsonAsync(
            $"/api/iam/v1/users/{created.UserId}",
            new { loginName = "operator-updated", email = "operator.updated@nerv-iip.local", enabled = true });
        patch.EnsureSuccessStatusCode();
        var updated = await ReadResponseDataAsync<UserResponse>(patch);

        Assert.Equal(created.UserId, updated.UserId);
        Assert.Equal("operator-updated", updated.LoginName);
        Assert.Equal("operator.updated@nerv-iip.local", updated.Email);
        Assert.True(updated.Enabled);

        var disable = await client.PostAsync($"/api/iam/v1/users/{created.UserId}/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == new UserId(created.UserId));

            Assert.Equal("operator-updated", user.LoginName);
            Assert.Equal("operator.updated@nerv-iip.local", user.Email);
            Assert.False(user.Enabled);
            Assert.DoesNotContain("Operator123!", user.PasswordHash, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Postgres_profile_persists_role_mutation_permission_catalog_and_password_reset()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth!.AccessToken);

        var catalogResponse = await client.GetAsync("/api/iam/v1/permissions");
        catalogResponse.EnsureSuccessStatusCode();
        var catalog = await ReadResponseDataAsync<PermissionCatalogResponse>(catalogResponse);
        Assert.Contains(catalog!.Items, item => item.Code == "iam.roles.manage" && item.Seeded);

        var createRole = await client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "Ops Operator", permissionCodes = new[] { "iam.roles.read", "ops.tasks.read" } });
        Assert.Equal(HttpStatusCode.Created, createRole.StatusCode);
        var role = await ReadResponseDataAsync<RoleResponse>(createRole);

        var duplicateRole = await client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "ops operator", permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateRole.StatusCode);

        var patchRole = await client.PatchAsJsonAsync(
            $"/api/iam/v1/roles/{role!.RoleId}/permissions",
            new { permissionCodes = new[] { "iam.users.read", "ops.tasks.read" } });
        patchRole.EnsureSuccessStatusCode();

        var patchAdminRoleScopes = await client.PatchAsJsonAsync(
            "/api/iam/v1/roles/role-platform-admin/data-scopes",
            new { dataScopes = new[] { new { scopeType = "workshop", scopeCode = "WS-PG" } } });
        patchAdminRoleScopes.EnsureSuccessStatusCode();
        var adminRoleScopes = await ReadResponseDataAsync<DataScopeListResponse>(patchAdminRoleScopes);
        Assert.Equal([new DataScopeResponse("workshop", "WS-PG")], adminRoleScopes!.DataScopes);

        var patchAdminMembershipScopes = await client.PatchAsJsonAsync(
            "/api/iam/v1/users/user-admin/membership-data-scopes",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                dataScopes = new[] { new { scopeType = "site", scopeCode = "SITE-PG" } },
            });
        patchAdminMembershipScopes.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var effectiveScopes = await new MembershipRepository(db).ListEffectiveDataScopesAsync(
                new UserId("user-admin"),
                new OrganizationId("org-001"),
                new IamEnvironmentId("env-dev"),
                CancellationToken.None);
            Assert.Contains(effectiveScopes, x => x.ScopeType == "site" && x.ScopeCode == "SITE-PG");
            Assert.Contains(effectiveScopes, x => x.ScopeType == "workshop" && x.ScopeCode == "WS-PG");
        }

        var createUser = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName = "reset-pg-user", email = "reset-pg-user@nerv-iip.local", password = "OldPassword123!" });
        Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);
        var user = await ReadResponseDataAsync<UserResponse>(createUser);

        var oldUserLogin = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "reset-pg-user", password = "OldPassword123!" });
        oldUserLogin.EnsureSuccessStatusCode();
        var oldAuth = await ReadResponseDataAsync<AuthResponse>(oldUserLogin);

        var reset = await client.PostAsJsonAsync(
            $"/api/iam/v1/users/{user!.UserId}/reset-password",
            new { newPassword = "NewPassword123!" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", oldAuth!.AccessToken);
        var staleMe = await client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);

        var staleRefresh = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/refresh",
            new { refreshToken = oldAuth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, staleRefresh.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var oldPasswordLogin = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "reset-pg-user", password = "OldPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "reset-pg-user", password = "NewPassword123!" });
        newPasswordLogin.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persistedRole = await db.Roles
                .Include(x => x.Permissions)
                .SingleAsync(x => x.Id == new RoleId(role.RoleId));
            Assert.Equal(
                ["iam.users.read", "ops.tasks.read"],
                persistedRole.Permissions.Select(x => x.PermissionCode).Order().ToArray());

            var resetUser = await db.Users.SingleAsync(x => x.Id == new UserId(user.UserId));
            Assert.DoesNotContain("OldPassword123!", resetUser.PasswordHash, StringComparison.Ordinal);
            Assert.DoesNotContain("NewPassword123!", resetUser.PasswordHash, StringComparison.Ordinal);
            Assert.Equal(2, resetUser.PermissionVersion);
        }
    }

    [Fact]
    public async Task Postgres_user_lifecycle_and_password_policy_use_ef_persistence()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var adminLogin = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "admin", password = "Admin123!" });
        adminLogin.EnsureSuccessStatusCode();
        var adminAuth = await ReadResponseDataAsync<AuthResponse>(adminLogin);
        client.DefaultRequestHeaders.Authorization = new("Bearer", adminAuth.AccessToken);

        var create = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new
            {
                loginName = "lifecycle-pg-user",
                email = "lifecycle-pg-user@nerv-iip.local",
                password = "InitialPassword123!"
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var user = await ReadResponseDataAsync<UserResponse>(create);

        var userLogin = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "lifecycle-pg-user", password = "InitialPassword123!" });
        userLogin.EnsureSuccessStatusCode();
        var userAuth = await ReadResponseDataAsync<LifecycleAuthResponse>(userLogin);
        Assert.True(userAuth.PasswordChangeRequired);

        client.DefaultRequestHeaders.Authorization = new("Bearer", userAuth.AccessToken);
        var change = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/change-password",
            new { currentPassword = "InitialPassword123!", newPassword = "ChangedPassword123!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var oldBearerChange = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/change-password",
            new { currentPassword = "ChangedPassword123!", newPassword = "AnotherPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldBearerChange.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", adminAuth.AccessToken);
        var disableViaPatch = await client.PatchAsJsonAsync(
            $"/api/iam/v1/users/{user.UserId}",
            new
            {
                loginName = user.LoginName,
                email = user.Email,
                enabled = false,
                accountExpiresAtUtc = (DateTimeOffset?)null
            });
        disableViaPatch.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var typedUserId = new UserId(user.UserId);
            var activeSessions = await db.UserSessions
                .Where(x => x.UserId == typedUserId && x.RevokedAtUtc == null)
                .ToListAsync();
            Assert.Empty(activeSessions);
        }

        client.DefaultRequestHeaders.Authorization = null;
        var disabledRefresh = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/refresh",
            new { userAuth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, disabledRefresh.StatusCode);

        var disabledLogin = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "lifecycle-pg-user", password = "ChangedPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, disabledLogin.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", adminAuth.AccessToken);
        var historyUser = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new
            {
                loginName = "history-pg-user",
                email = "history-pg-user@nerv-iip.local",
                password = "HistoryPassword123!"
            });
        Assert.Equal(HttpStatusCode.Created, historyUser.StatusCode);
        var history = await ReadResponseDataAsync<UserResponse>(historyUser);

        var reset = await client.PostAsJsonAsync(
            $"/api/iam/v1/users/{history.UserId}/reset-password",
            new { newPassword = "HistoryPassword234!" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var reuse = await client.PostAsJsonAsync(
            $"/api/iam/v1/users/{history.UserId}/reset-password",
            new { newPassword = "HistoryPassword123!" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var typedHistoryUserId = new UserId(history.UserId);
            var historyRows = await db.Set<UserPasswordHistory>()
                .Where(x => x.UserId == typedHistoryUserId)
                .ToListAsync();
            Assert.NotEmpty(historyRows);
        }
    }

    [Fact]
    public async Task Postgres_profile_issues_external_client_token_and_authorizes_with_grants()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret")
            .SetEnvironmentVariable("Iam__Seed__ExternalClientSecret", "external-client-secret");

        await using var factory = new WebApplicationFactory<Program>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();

            var migrations = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
            await migrations.MigrateAsync();

            var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
            await seed.SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var token = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/client-token",
            new { clientId = "external-client-demo", clientSecret = "external-client-secret", scope = "ops.tasks.create" });
        token.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<ClientCredentialsTokenResponse>(token);

        Assert.Equal("Bearer", auth!.TokenType);
        Assert.Equal("ops.tasks.create", auth.Scope);
        Assert.True(auth.ExpiresAtUtc > DateTimeOffset.UtcNow);

        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var me = await client.GetAsync("/api/iam/v1/me");
        me.EnsureSuccessStatusCode();
        var principal = await ReadResponseDataAsync<MeResponse>(me);
        Assert.Equal("external-client-demo", principal!.UserId);
        Assert.Equal("external-client", principal.PrincipalType);

        var allowed = await client.PostAsJsonAsync(
            "/internal/iam/v1/authorization/check",
            new { organizationId = "org-001", environmentId = "env-dev", permissionCode = "ops.tasks.create" });
        allowed.EnsureSuccessStatusCode();

        var denied = await client.PostAsJsonAsync(
            "/internal/iam/v1/authorization/check",
            new { organizationId = "org-001", environmentId = "env-dev", permissionCode = "iam.users.manage" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.ExternalClients.CountAsync(x => x.ClientId == "external-client-demo"));
            Assert.Equal(1, await db.AuthorizationGrants.CountAsync(x =>
                x.PrincipalType == "external-client"
                && x.PrincipalId == "external-client-demo"
                && x.PermissionCode == "ops.tasks.create"));
        }
    }

    [Fact]
    public async Task Postgres_automigrate_is_rejected_outside_development()
    {
        // The scope serialises every process-global mutator in the assembly and restores each
        // variable's exact prior value (including "was never set") on dispose.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("Persistence__AutoMigrate", "true")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", "Host=localhost;Database=nerv_iip_iam_guard;Username=nerv;Password=nerv")
            .SetEnvironmentVariable("Iam__Jwt__SigningKeys__0__Kid", IamJwtTestKeys.Kid)
            .SetEnvironmentVariable("Iam__Jwt__SigningKeys__0__PrivateKeyPem", IamJwtTestKeys.PrivateKeyPem);

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Persistence:AutoMigrate=true", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<bool> IsUniqueLowerExpressionIndexAsync(
        ApplicationDbContext db,
        string indexName,
        string columnName)
    {
        return await db.Database.SqlQueryRaw<bool>(
            """
            select i.indisunique
               and pg_get_indexdef(i.indexrelid) like '%lower(%'
               and pg_get_indexdef(i.indexrelid) like '%' || quote_ident({1}) || '%' as "Value"
            from pg_index i
            join pg_class index_class on index_class.oid = i.indexrelid
            join pg_class table_class on table_class.oid = i.indrelid
            join pg_namespace table_namespace on table_namespace.oid = table_class.relnamespace
            where table_namespace.nspname = 'iam'
              and table_class.relname = 'users'
              and index_class.relname = {0}
            """,
            indexName,
            columnName).SingleOrDefaultAsync();
    }

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }

    private sealed class FailNextSessionInsertInterceptor : DbCommandInterceptor
    {
        public bool FailNextSessionInsert { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSessionInsert
                && command.CommandText.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("user_sessions", StringComparison.OrdinalIgnoreCase))
            {
                FailNextSessionInsert = false;
                throw new TimeoutException("Injected rotated session insert failure.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PauseAfterArmedTransactionCommitInterceptor : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> commitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;
        private int claimed;

        public void Arm()
        {
            Volatile.Write(ref armed, 1);
        }

        public Task WaitUntilCommitPausedAsync()
        {
            return commitPaused.Task;
        }

        public void Release()
        {
            release.TrySetResult(true);
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref armed) == 1
                && Interlocked.CompareExchange(ref claimed, 1, 0) == 0)
            {
                commitPaused.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Test mediator cannot create streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Test mediator cannot create streams.");
        }
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
    private sealed record LifecycleAuthResponse(
        string AccessToken,
        string RefreshToken,
        string SessionId,
        DateTimeOffset ExpiresAtUtc,
        bool PasswordChangeRequired);
    private sealed record ClientCredentialsTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Scope);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
    private sealed record UserResponse(
        string UserId,
        string LoginName,
        string Email,
        bool Enabled,
        DateTimeOffset? AccountExpiresAtUtc = null,
        bool PasswordChangeRequired = false,
        DateTimeOffset? PasswordExpiresAtUtc = null,
        DateTimeOffset? LockoutUntilUtc = null);
    private sealed record RoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
    private sealed record PermissionCatalogResponse(IReadOnlyList<PermissionCatalogItemResponse> Items);
    private sealed record PermissionCatalogItemResponse(string Code, string Domain, string Description, bool Seeded);
    private sealed record MeResponse(
        string UserId,
        string LoginName,
        string Email,
        string PrincipalType,
        string OrganizationId,
        string EnvironmentId,
        int PermissionVersion);
    private sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
}
