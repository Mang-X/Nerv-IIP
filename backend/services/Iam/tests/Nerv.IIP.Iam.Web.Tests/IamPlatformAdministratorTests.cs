using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;
using NetCorePal.Extensions.Primitives;

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
        Assert.Equal("平台管理员账号不能停用。", (await disable.Content.ReadFromJsonAsync<ResponseDataEnvelope<object>>())!.Message);

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
    public async Task Created_user_can_sign_in_after_being_assigned_a_role_in_the_current_organization_environment()
    {
        var admin = await LoginAsync(_client, "admin", "Admin123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var created = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName = "assigned-operator", email = "assigned-operator@nerv-iip.local", password = "Operator123!" });
        created.EnsureSuccessStatusCode();
        var userId = (await created.Content.ReadFromJsonAsync<ResponseDataEnvelope<CreatedUser>>())!.Data!.UserId;

        // 还没分配角色：密码对时明确告诉用户「未分配组织或角色」，密码错时仍只说口令错误，不泄露账号状态。
        await AssertLoginRejectedAsync("assigned-operator", "Operator123!", "iam-no-membership");
        await AssertLoginRejectedAsync("assigned-operator", "Wrong123!", "iam-invalid-credentials");

        var assign = await _client.PutAsJsonAsync($"/api/iam/v1/users/{userId}/membership", new { roleIds = new[] { "role-erp-sales" } });
        assign.EnsureSuccessStatusCode();
        var membership = (await assign.Content.ReadFromJsonAsync<ResponseDataEnvelope<Membership>>())!.Data!;
        Assert.Equal("org-001", membership.OrganizationId);
        Assert.Equal("env-dev", membership.EnvironmentId);
        Assert.Equal(["role-erp-sales"], membership.RoleIds);
        _client.DefaultRequestHeaders.Authorization = null;

        var me = await GetMeAsync((await LoginAsync(_client, "assigned-operator", "Operator123!")).AccessToken);
        me.EnsureSuccessStatusCode();
        var principal = (await me.Content.ReadFromJsonAsync<ResponseDataEnvelope<Principal>>())!.Data!;
        Assert.Equal("org-001", principal.OrganizationId);
        Assert.Equal("env-dev", principal.EnvironmentId);
        Assert.Equal(["role-erp-sales"], principal.RoleIds);
        Assert.Equal(
            NervIipSeedRoles.ErpJobRoles.Single(x => x.RoleId == "role-erp-sales").PermissionCodes.Order(StringComparer.Ordinal),
            principal.PermissionCodes);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var remove = await _client.PutAsJsonAsync($"/api/iam/v1/users/{userId}/membership", new { roleIds = Array.Empty<string>() });
        remove.EnsureSuccessStatusCode();
        _client.DefaultRequestHeaders.Authorization = null;
        await AssertLoginRejectedAsync("assigned-operator", "Operator123!", "iam-no-membership");
    }

    [Fact]
    public async Task Platform_administrator_cannot_lose_the_administrator_role_through_membership_assignment()
    {
        var admin = await LoginAsync(_client, "admin", "Admin123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var removeMembership = await _client.PutAsJsonAsync("/api/iam/v1/users/user-admin/membership", new { roleIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, removeMembership.StatusCode);

        var swapRole = await _client.PutAsJsonAsync("/api/iam/v1/users/user-admin/membership", new { roleIds = new[] { "role-erp-sales" } });
        Assert.Equal(HttpStatusCode.BadRequest, swapRole.StatusCode);

        var addRole = await _client.PutAsJsonAsync(
            "/api/iam/v1/users/user-admin/membership",
            new { roleIds = new[] { "role-platform-admin", "role-erp-sales" } });
        addRole.EnsureSuccessStatusCode();
        var restore = await _client.PutAsJsonAsync("/api/iam/v1/users/user-admin/membership", new { roleIds = new[] { "role-platform-admin" } });
        restore.EnsureSuccessStatusCode();

        var current = await _client.GetFromJsonAsync<ResponseDataEnvelope<Membership>>("/api/iam/v1/users/user-admin/membership");
        Assert.Equal(["role-platform-admin"], current!.Data!.RoleIds);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [IamRealPostgresFact]
    public async Task Production_startup_bootstraps_administrator_default_tenant_and_planner_role()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
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
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Roles.Add(new Role(new RoleId("role-existing-planner"), "生产计划员", ["business.planning.mps.read"]));
            await db.SaveChangesAsync();
        }

        await using (var withoutPassword = ProductionFactory(adminPassword: null))
        {
            var missing = Assert.Throws<InvalidOperationException>(() => withoutPassword.CreateClient());
            Assert.Contains("Iam:Seed:AdminPassword", missing.Message, StringComparison.Ordinal);
        }

        await using (var weakPassword = ProductionFactory("abc"))
        {
            var rejected = Assert.Throws<KnownException>(() => weakPassword.CreateClient());
            Assert.StartsWith("Password must", rejected.Message, StringComparison.Ordinal);
        }

        await using (var factory = ProductionFactory("Bootstrap123!"))
        {
            var client = factory.CreateClient();
            var auth = await LoginAsync(client, "admin", "Bootstrap123!");
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
            await LoginAsync(client, "admin", "Bootstrap123!");

            using var scope = restarted.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal("user-admin", Assert.Single(await db.Users.ToListAsync()).Id.Id);
            Assert.Equal("org-001", Assert.Single(await db.Organizations.ToListAsync()).Id.Id);
            Assert.Equal("env-dev", Assert.Single(await db.Environments.ToListAsync()).Id.Id);
            var roles = await db.Roles.ToListAsync();
            Assert.Equal(
                ["role-existing-planner", "role-platform-admin", "role-production-planner"],
                roles.Select(role => role.Id.Id).Order(StringComparer.Ordinal));
            Assert.Equal("生产计划员", roles.Single(role => role.Id == new RoleId("role-existing-planner")).RoleName);
            Assert.Equal("生产计划员（系统预置）", roles.Single(role => role.Id == new RoleId("role-production-planner")).RoleName);
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

    private async Task<HttpResponseMessage> GetMeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/iam/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task AssertLoginRejectedAsync(string loginName, string password, string failureCode)
    {
        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName, password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(failureCode, login.Headers.GetValues("X-Nerv-Iam-Login-Failure").Single());
    }

    private static async Task<AuthResponse> LoginAsync(HttpClient client, string loginName, string password)
    {
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName, password });
        login.EnsureSuccessStatusCode();
        var envelope = await login.Content.ReadFromJsonAsync<ResponseDataEnvelope<AuthResponse>>();
        Assert.NotNull(envelope?.Data);
        return envelope.Data;
    }

    private sealed record AuthResponse(string AccessToken, bool PasswordChangeRequired);
    private sealed record CreatedUser(string UserId);
    private sealed record Membership(string OrganizationId, string EnvironmentId, IReadOnlyList<string> RoleIds);
    private sealed record Principal(string OrganizationId, string EnvironmentId, IReadOnlyList<string> RoleIds, IReadOnlyList<string> PermissionCodes);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}

internal sealed class IamRealPostgresFactAttribute : FactAttribute
{
    public IamRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL IAM production bootstrap proof.";
        }
    }
}
