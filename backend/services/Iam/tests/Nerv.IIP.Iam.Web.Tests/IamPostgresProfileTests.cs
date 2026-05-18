using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Seed;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamPostgresProfileTests
{
    [Fact]
    public async Task Postgres_profile_seeds_admin_and_persists_login_refresh_logout_and_connector_validation()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "ConnectionStrings__IamDb",
            "Iam__Seed__Enabled",
            "Iam__Seed__AdminPassword",
            "Iam__Seed__ConnectorHostSecret");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("ConnectionStrings__IamDb", postgresConnectionString);
            Environment.SetEnvironmentVariable("Iam__Seed__Enabled", "true");
            Environment.SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!");
            Environment.SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

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

            var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
            login.EnsureSuccessStatusCode();
            var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();

            Assert.NotNull(auth);
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
            var principal = await me.Content.ReadFromJsonAsync<MeResponse>();
            Assert.Equal("user-admin", principal!.UserId);
            Assert.Equal("admin", principal.LoginName);
            Assert.Equal("user", principal.PrincipalType);
            Assert.Equal("org-001", principal.OrganizationId);
            Assert.Equal("env-dev", principal.EnvironmentId);
            Assert.Equal(1, principal.PermissionVersion);
            Assert.True(auth.ExpiresAtUtc > DateTimeOffset.UtcNow);

            var refresh = await client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
            refresh.EnsureSuccessStatusCode();
            var rotated = await refresh.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(rotated);
            Assert.False(string.IsNullOrWhiteSpace(rotated.AccessToken));
            Assert.False(string.IsNullOrWhiteSpace(rotated.SessionId));
            Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);

            var oldRefresh = await client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

            client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
            var meWithRotatedAccessToken = await client.GetAsync("/api/iam/v1/me");
            meWithRotatedAccessToken.EnsureSuccessStatusCode();

            var logout = await client.PostAsJsonAsync("/api/iam/v1/auth/logout", new { sessionId = rotated.SessionId });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

            var meAfterLogout = await client.GetAsync("/api/iam/v1/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);

            client.DefaultRequestHeaders.Authorization = null;
            var secondLogin = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
            secondLogin.EnsureSuccessStatusCode();
            var secondAuth = await secondLogin.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(secondAuth);

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
            var connectorPrincipal = await connector.Content.ReadFromJsonAsync<ConnectorPrincipalResponse>();
            Assert.Equal("connector-host", connectorPrincipal!.PrincipalType);
            Assert.Equal("connector-host-001", connectorPrincipal.ConnectorHostId);
            Assert.Equal("org-001", connectorPrincipal.OrganizationId);
            Assert.Equal("env-dev", connectorPrincipal.EnvironmentId);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var adminUsers = await db.Users.Where(x => x.LoginName == "admin").ToListAsync();
                var admin = Assert.Single(adminUsers);
                Assert.DoesNotContain("Admin123!", admin.PasswordHash, StringComparison.Ordinal);

                var connectorCredentials = await db.ConnectorHostCredentials
                    .Where(x => x.ConnectorHostId == "connector-host-001")
                    .ToListAsync();
                Assert.Single(connectorCredentials);
            }
        }
        finally
        {
            RestoreEnvironment(environment);
        }
    }

    [Fact]
    public void Postgres_automigrate_is_rejected_outside_development()
    {
        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "Persistence__AutoMigrate",
            "ConnectionStrings__IamDb",
            "Iam__Jwt__SigningKey");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("Persistence__AutoMigrate", "true");
            Environment.SetEnvironmentVariable("ConnectionStrings__IamDb", "Host=localhost;Database=nerv_iip_iam_guard;Username=nerv;Password=nerv");
            Environment.SetEnvironmentVariable("Iam__Jwt__SigningKey", "production-test-signing-key-that-is-long-enough");

            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("Persistence:AutoMigrate=true", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(environment);
        }
    }

    private static IReadOnlyDictionary<string, string?> PreserveEnvironment(params string[] names)
    {
        return names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
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
