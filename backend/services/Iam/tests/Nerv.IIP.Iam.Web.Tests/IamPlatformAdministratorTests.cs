using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamPlatformAdministratorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public IamPlatformAdministratorTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Platform_administrator_cannot_be_disabled_or_expired()
    {
        var disable = await _client.PostAsync("/api/iam/v1/users/user-admin/disable", null);
        Assert.Equal(HttpStatusCode.BadRequest, disable.StatusCode);

        var patchDisabled = await _client.PatchAsJsonAsync(
            "/api/iam/v1/users/user-admin",
            new { loginName = "admin", email = "admin@nerv-iip.local", enabled = false });
        Assert.Equal(HttpStatusCode.BadRequest, patchDisabled.StatusCode);

        var patchExpiry = await _client.PatchAsJsonAsync(
            "/api/iam/v1/users/user-admin",
            new { loginName = "admin", email = "admin@nerv-iip.local", enabled = true, accountExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, patchExpiry.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Platform_administrator_role_cannot_lose_permissions_or_default_organization_scope()
    {
        var removePermission = await _client.PatchAsJsonAsync(
            "/api/iam/v1/roles/role-platform-admin/permissions",
            new { permissionCodes = NervIipSeedPermissions.All.Where(x => x != "iam.users.manage").ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, removePermission.StatusCode);

        var keepAllPermissions = await _client.PatchAsJsonAsync(
            "/api/iam/v1/roles/role-platform-admin/permissions",
            new { permissionCodes = NervIipSeedPermissions.All });
        keepAllPermissions.EnsureSuccessStatusCode();

        var narrowScope = await _client.PatchAsJsonAsync(
            "/api/iam/v1/roles/role-platform-admin/data-scopes",
            new { dataScopes = new[] { new { scopeType = "workshop", scopeCode = "WS-A" } } });
        Assert.Equal(HttpStatusCode.BadRequest, narrowScope.StatusCode);

        var widenScope = await _client.PatchAsJsonAsync(
            "/api/iam/v1/roles/role-platform-admin/data-scopes",
            new
            {
                dataScopes = new[]
                {
                    new { scopeType = "organization", scopeCode = "org-001" },
                    new { scopeType = "workshop", scopeCode = "WS-A" },
                },
            });
        widenScope.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Production_startup_bootstraps_only_the_platform_administrator_and_its_default_tenant()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return;
        }

        await using var database = await PostgreSqlTestDatabase.CreateAsync(postgresConnectionString, "nerv_iam_bootstrap");
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", database.ConnectionString);

        await using (var migrator = new WebApplicationFactory<Program>())
        {
            using var scope = migrator.Services.CreateScope();
            database.AssertOwns(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString());
            await scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>().MigrateAsync();
        }

        await using (var withoutPassword = ProductionFactory(adminPassword: null))
        {
            var missing = Assert.Throws<InvalidOperationException>(() => withoutPassword.CreateClient());
            Assert.Contains("Iam:Seed:AdminPassword", missing.Message, StringComparison.Ordinal);
        }

        await using (var factory = ProductionFactory("Bootstrap123!"))
        {
            var client = factory.CreateClient();
            var auth = await LoginAsync(client, "Bootstrap123!");
            Assert.True(auth.PasswordChangeRequired);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            var disable = await client.PostAsync("/api/iam/v1/users/user-admin/disable", null);
            Assert.Equal(HttpStatusCode.BadRequest, disable.StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
        }

        // 第二次启动换了配置口令：已存在的管理员不被覆盖。
        await using (var restarted = ProductionFactory("Rotated123!"))
        {
            var client = restarted.CreateClient();
            await LoginAsync(client, "Bootstrap123!");

            using var scope = restarted.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal("user-admin", Assert.Single(await db.Users.ToListAsync()).Id.Id);
            Assert.Equal("org-001", Assert.Single(await db.Organizations.ToListAsync()).Id.Id);
            Assert.Equal("env-dev", Assert.Single(await db.Environments.ToListAsync()).Id.Id);
            Assert.Equal("role-platform-admin", Assert.Single(await db.Roles.ToListAsync()).Id.Id);
            Assert.Equal(1, await db.Memberships.CountAsync());
            Assert.Equal(0, await db.ConnectorHostCredentials.CountAsync());
            Assert.Equal(0, await db.ExternalClients.CountAsync());
            Assert.Equal(0, await db.AuthorizationGrants.CountAsync());
        }
    }

    private static WebApplicationFactory<Program> ProductionFactory(string? adminPassword)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKeys:0:Kid", IamJwtTestKeys.Kid);
                builder.UseSetting("Iam:Jwt:SigningKeys:0:PrivateKeyPem", IamJwtTestKeys.PrivateKeyPem);
                builder.UseSetting("Iam:Secrets:Pepper", "test-production-pepper");
                builder.UseSetting("Iam:EnterpriseIdentity:Mfa:DevelopmentCode", "654321");
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                if (adminPassword is not null)
                {
                    builder.UseSetting("Iam:Seed:AdminPassword", adminPassword);
                }
            });
    }

    private static async Task<AuthResponse> LoginAsync(HttpClient client, string password)
    {
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password });
        login.EnsureSuccessStatusCode();
        var envelope = await login.Content.ReadFromJsonAsync<ResponseDataEnvelope<AuthResponse>>();
        Assert.NotNull(envelope?.Data);
        return envelope.Data;
    }

    private sealed record AuthResponse(string AccessToken, bool PasswordChangeRequired);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
