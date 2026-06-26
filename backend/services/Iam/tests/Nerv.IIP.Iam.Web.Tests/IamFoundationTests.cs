using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IamFoundationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Admin_can_login_refresh_logout_and_validate_connector_host()
    {
        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));

        var refresh = await _client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await ReadResponseDataAsync<AuthResponse>(refresh);
        Assert.NotEqual(auth.RefreshToken, rotated!.RefreshToken);

        var oldRefresh = await _client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

        var connector = await _client.PostAsJsonAsync("/api/iam/v1/connectors/credentials/validate", new { connectorHostId = "connector-host-001", secret = "local-connector-secret" });
        connector.EnsureSuccessStatusCode();
        var connectorPrincipal = await ReadResponseDataAsync<ConnectorPrincipalResponse>(connector);
        Assert.Equal("connector-host", connectorPrincipal!.PrincipalType);
        Assert.Equal("org-001", connectorPrincipal.OrganizationId);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        var meBeforeLogout = await _client.GetAsync("/api/iam/v1/me");
        meBeforeLogout.EnsureSuccessStatusCode();
        var principal = await ReadResponseDataAsync<MeResponse>(meBeforeLogout);

        Assert.Equal("user-admin", principal!.UserId);
        Assert.Equal("admin", principal.LoginName);
        Assert.Equal("user", principal.PrincipalType);
        Assert.Equal("org-001", principal.OrganizationId);
        Assert.Equal("env-dev", principal.EnvironmentId);
        Assert.Equal(1, principal.PermissionVersion);
        Assert.True(rotated.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var logout = await _client.PostAsJsonAsync("/api/iam/v1/auth/logout", new { sessionId = rotated.SessionId });
        logout.EnsureSuccessStatusCode();

        var me = await _client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Console_smoke_login_returns_gateway_compatible_jwt_access_token()
    {
        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<AuthResponse>(login);

        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            auth!.AccessToken,
            CreateGatewayTokenValidationParameters(),
            out var securityToken);

        var jwt = Assert.IsType<JwtSecurityToken>(securityToken);
        Assert.Equal("nerv-iip-iam", jwt.Issuer);
        Assert.Contains(jwt.Audiences, audience => audience == "nerv-iip-api");
        Assert.Equal("user-admin", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(auth.SessionId, principal.FindFirst("sessionId")?.Value);
        Assert.Equal("user", principal.FindFirst("principalType")?.Value);
        Assert.Equal("admin", principal.FindFirst("loginName")?.Value);
        Assert.Equal("admin@nerv-iip.local", principal.FindFirst("email")?.Value);
        Assert.Equal("org-001", principal.FindFirst("organizationId")?.Value);
        Assert.Equal("env-dev", principal.FindFirst("environmentId")?.Value);
        Assert.Equal("1", principal.FindFirst("permissionVersion")?.Value);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var me = await _client.GetAsync("/api/iam/v1/me");

        me.EnsureSuccessStatusCode();
    }

    [Fact]
    public void In_memory_store_login_and_refresh_issue_gateway_compatible_jwt_access_tokens()
    {
        var store = _factory
            .Services
            .GetRequiredService<InMemoryIamStore>();
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var login = store.Login("admin", "Admin123!");
        var loginPrincipal = tokenHandler.ValidateToken(
            login.AccessToken,
            CreateGatewayTokenValidationParameters(),
            out var loginSecurityToken);
        var loginJwt = Assert.IsType<JwtSecurityToken>(loginSecurityToken);

        Assert.Equal("nerv-iip-iam", loginJwt.Issuer);
        Assert.Equal("user-admin", loginPrincipal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(login.SessionId, loginPrincipal.FindFirst("sessionId")?.Value);
        Assert.Equal("org-001", loginPrincipal.FindFirst("organizationId")?.Value);
        Assert.Equal("env-dev", loginPrincipal.FindFirst("environmentId")?.Value);
        AssertAccessTokenExpiresAt(loginPrincipal, login.ExpiresAtUtc);

        var refresh = store.Refresh(login.RefreshToken);
        var refreshPrincipal = tokenHandler.ValidateToken(
            refresh.AccessToken,
            CreateGatewayTokenValidationParameters(),
            out var refreshSecurityToken);
        var refreshJwt = Assert.IsType<JwtSecurityToken>(refreshSecurityToken);

        Assert.Equal("nerv-iip-iam", refreshJwt.Issuer);
        Assert.Equal("user-admin", refreshPrincipal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(refresh.SessionId, refreshPrincipal.FindFirst("sessionId")?.Value);
        AssertAccessTokenExpiresAt(refreshPrincipal, refresh.ExpiresAtUtc);
        Assert.NotEqual(login.RefreshToken, refresh.RefreshToken);
    }

    [Fact]
    public async Task External_client_credentials_issue_external_client_token_and_authorization_check_uses_grants()
    {
        var token = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/client-token",
            new { clientId = "external-client-demo", clientSecret = "external-client-secret", scope = "ops.tasks.create" });
        token.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<ClientCredentialsTokenResponse>(token);

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Equal("Bearer", auth.TokenType);
        Assert.True(auth.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            auth.AccessToken,
            CreateGatewayTokenValidationParameters(),
            out _);

        Assert.Equal("external-client-demo", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal("external-client", principal.FindFirst("principalType")?.Value);
        Assert.Equal("org-001", principal.FindFirst("organizationId")?.Value);
        Assert.Equal("env-dev", principal.FindFirst("environmentId")?.Value);
        Assert.Equal("ops.tasks.create", principal.FindFirst("scope")?.Value);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var me = await _client.GetAsync("/api/iam/v1/me");
        me.EnsureSuccessStatusCode();
        var current = await ReadResponseDataAsync<MeResponse>(me);
        Assert.Equal("external-client-demo", current!.UserId);
        Assert.Equal("external-client", current.PrincipalType);

        var allowed = await _client.PostAsJsonAsync(
            "/internal/iam/v1/authorization/check",
            new { organizationId = "org-001", environmentId = "env-dev", permissionCode = "ops.tasks.create" });
        allowed.EnsureSuccessStatusCode();

        var denied = await _client.PostAsJsonAsync(
            "/internal/iam/v1/authorization/check",
            new { organizationId = "org-001", environmentId = "env-dev", permissionCode = "iam.users.manage" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task In_memory_user_management_creates_updates_and_disables_users()
    {
        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName = "operator", email = "operator@nerv-iip.local", password = "Operator123!" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadResponseDataAsync<UserResponse>(create);

        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.UserId));
        Assert.Equal("operator", created.LoginName);
        Assert.Equal("operator@nerv-iip.local", created.Email);
        Assert.True(created.Enabled);

        var patch = await _client.PatchAsJsonAsync(
            $"/api/iam/v1/users/{created.UserId}",
            new { loginName = "operator-updated", email = "operator.updated@nerv-iip.local", enabled = true });
        patch.EnsureSuccessStatusCode();
        var updated = await ReadResponseDataAsync<UserResponse>(patch);

        Assert.Equal(created.UserId, updated!.UserId);
        Assert.Equal("operator-updated", updated.LoginName);
        Assert.Equal("operator.updated@nerv-iip.local", updated.Email);
        Assert.True(updated.Enabled);

        var disable = await _client.PostAsync($"/api/iam/v1/users/{created.UserId}/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        var users = await GetPagedUsersAsync("/api/iam/v1/users?pageIndex=1&pageSize=50");
        var disabled = Assert.Single(users!.Items, user => user.UserId == created.UserId);
        Assert.False(disabled.Enabled);
    }

    [Fact]
    public async Task Internal_worker_directory_lists_users_without_user_iam_admin_permission()
    {
        var anonymous = await _client.GetAsync("/internal/iam/v1/workers?filterSearch=admin&pageIndex=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/internal/iam/v1/workers?filterSearch=admin&pageIndex=1&pageSize=10&filterEnabled=true");
        request.Headers.Authorization = new("Bearer", "local-internal-service-token");

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var workers = await ReadResponseDataAsync<PagedListResponse<WorkerDirectoryUserResponse>>(response);
        var admin = Assert.Single(workers!.Items);
        Assert.Equal("user-admin", admin.UserId);
        Assert.Equal("admin", admin.DisplayName);
        Assert.Null(admin.EmployeeNo);
        Assert.Null(admin.Department);
        Assert.Equal("active", admin.Status);
    }

    [Fact]
    public async Task In_memory_role_management_creates_role_updates_permissions_and_lists_catalog()
    {
        var catalogResponse = await _client.GetAsync("/api/iam/v1/permissions");
        catalogResponse.EnsureSuccessStatusCode();
        var catalog = await ReadResponseDataAsync<PermissionCatalogResponse>(catalogResponse);
        Assert.Contains(catalog!.Items, item => item.Code == "iam.roles.manage" && item.Domain == "iam");
        Assert.Contains(catalog.Items, item => item.Code == "business.quality.inspection-records.create"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.inventory.ledger.read"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.erp.procurement.manage"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.erp.sales.manage"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.erp.finance.manage"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.foundation.read"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.overview.read"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.work-orders.read"
            && item.Domain == "business"
            && item.Seeded);
        foreach (var permission in new[]
        {
            "business.mes.work-orders.manage",
            "business.mes.plans.read",
            "business.mes.materials.read",
            "business.mes.materials.manage",
            "business.mes.dispatch.read",
            "business.mes.dispatch.manage",
            "business.mes.operations.read",
            "business.mes.operations.manage",
            "business.mes.reporting.read",
            "business.mes.reporting.write",
            "business.mes.quality.read",
            "business.mes.quality.write",
            "business.mes.receipts.read",
            "business.mes.receipts.manage",
            "business.mes.downtime.read",
            "business.mes.downtime.manage",
            "business.mes.handovers.read",
            "business.mes.handovers.manage",
            "business.mes.traceability.read",
            "business.mes.schedules.read",
            "business.mes.schedules.manage",
            "business.mes.capacity.read"
        })
        {
            Assert.Contains(catalog.Items, item => item.Code == permission
                && item.Domain == "business"
                && item.Seeded);
        }

        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "Operator", permissionCodes = new[] { "apphub.instances.read", "business.inventory.ledger.read", "business.quality.inspection-records.create", "business.mes.work-orders.read" } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadResponseDataAsync<RoleResponse>(create);

        Assert.StartsWith("role-", created!.RoleId, StringComparison.Ordinal);
        Assert.Equal("Operator", created.RoleName);
        Assert.Equal(["apphub.instances.read", "business.inventory.ledger.read", "business.mes.work-orders.read", "business.quality.inspection-records.create"], created.PermissionCodes.Order().ToArray());

        var patch = await _client.PatchAsJsonAsync(
            $"/api/iam/v1/roles/{created.RoleId}/permissions",
            new { permissionCodes = new[] { "iam.users.read" } });
        patch.EnsureSuccessStatusCode();
        var updated = await ReadResponseDataAsync<RoleResponse>(patch);

        Assert.Equal(created.RoleId, updated!.RoleId);
        Assert.Equal(["iam.users.read"], updated.PermissionCodes);
    }

    [Fact]
    public async Task In_memory_role_management_rejects_unknown_permissions_and_duplicate_names()
    {
        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "Auditor", permissionCodes = new[] { "iam.users.read" } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var duplicate = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "auditor", permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var unknown = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "BadRole", permissionCodes = new[] { "iam.unknown" } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task In_memory_role_management_rejects_missing_blank_and_long_role_names()
    {
        var missing = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var nullName = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = (string?)null, permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, nullName.StatusCode);

        var blank = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = "   ", permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var tooLong = await _client.PostAsJsonAsync(
            "/api/iam/v1/roles",
            new { roleName = new string('x', 129), permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Admin_reset_password_changes_login_secret_and_revokes_sessions()
    {
        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName = "reset-user", email = "reset-user@nerv-iip.local", password = "OldPassword123!" });
        create.EnsureSuccessStatusCode();
        var user = await ReadResponseDataAsync<UserResponse>(create);

        var login = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "reset-user", password = "OldPassword123!" });
        login.EnsureSuccessStatusCode();
        var session = await ReadResponseDataAsync<AuthResponse>(login);

        var reset = await _client.PostAsJsonAsync(
            $"/api/iam/v1/users/{user!.UserId}/reset-password",
            new { newPassword = "NewPassword123!" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);
        var staleMe = await _client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;

        var oldLogin = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "reset-user", password = "OldPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName = "reset-user", password = "NewPassword123!" });
        newLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_lockout_blocks_password_attempts_after_consecutive_failures_and_success_resets_state()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var loginName = $"lockout-{suffix}";
        var password = "Lockout123!";
        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email = $"{loginName}@nerv-iip.local", password });
        create.EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var failed = await _client.PostAsJsonAsync(
                "/api/iam/v1/auth/login",
                new { loginName, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var resetLogin = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName, password });
        resetLogin.EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await _client.PostAsJsonAsync(
                "/api/iam/v1/auth/login",
                new { loginName, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var lockedLogin = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName, password });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedLogin.StatusCode);
    }

    [Fact]
    public async Task User_list_supports_page_filter_and_sort_parameters()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await CreateUserAsync($"alpha-{suffix}", $"alpha-{suffix}@nerv-iip.local");
        await CreateUserAsync($"beta-{suffix}", $"beta-{suffix}@nerv-iip.local");
        await CreateUserAsync($"gamma-{suffix}", $"gamma-{suffix}@nerv-iip.local");

        var users = await GetPagedUsersAsync(
            $"/api/iam/v1/users?pageIndex=2&pageSize=1&sortBy=loginName&sortOrder=desc&filterSearch={suffix}");

        Assert.NotNull(users);
        Assert.Equal(3, users.TotalCount);
        Assert.Equal(2, users.PageIndex);
        Assert.Equal(1, users.PageSize);
        var user = Assert.Single(users.Items);
        Assert.Equal($"beta-{suffix}", user.LoginName);
    }

    [Fact]
    public async Task User_list_clamps_paging_and_handles_empty_or_unknown_sort()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await CreateUserAsync($"delta-{suffix}", $"delta-{suffix}@nerv-iip.local");

        var empty = await GetPagedUsersAsync($"/api/iam/v1/users?filterSearch=missing-{suffix}");
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);

        var clampedPage = await GetPagedUsersAsync($"/api/iam/v1/users?pageIndex=0&pageSize=500&filterSearch={suffix}&sortBy=unknown");
        Assert.Equal(1, clampedPage.PageIndex);
        Assert.Equal(200, clampedPage.PageSize);
        Assert.Equal(1, clampedPage.TotalCount);
        Assert.Equal($"delta-{suffix}", Assert.Single(clampedPage.Items).LoginName);

        var negativePage = await GetPagedUsersAsync($"/api/iam/v1/users?pageIndex=-1&pageSize=-5&filterSearch={suffix}");
        Assert.Equal(1, negativePage.PageIndex);
        Assert.Equal(20, negativePage.PageSize);
        Assert.Equal(1, negativePage.TotalCount);
    }

    [Fact]
    public async Task Role_and_session_lists_return_paged_envelopes()
    {
        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();

        await AssertPagedEnvelopeAsync("/api/iam/v1/roles?pageIndex=1&pageSize=10&sortBy=roleName&sortOrder=asc&filterSearch=admin");
        await AssertPagedEnvelopeAsync("/api/iam/v1/sessions?pageIndex=1&pageSize=10&sortBy=issuedAtUtc&sortOrder=desc&filterSearch=user-admin&filterRevoked=false");
    }

    [Fact]
    public void In_memory_store_without_configured_token_issuer_fails_closed()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryIamStore(null!));
        var store = new InMemoryIamStore();

        var exception = Assert.Throws<InvalidOperationException>(() => store.Login("admin", "Admin123!"));

        Assert.Contains("token service", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_iam_requires_explicit_jwt_private_key_for_every_persistence_profile()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Iam:Jwt:SigningKeys:0:PrivateKeyPem", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_iam_requires_jwt_key_id()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKeys:0:PrivateKeyPem", IamJwtTestKeys.PrivateKeyPem);
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Iam:Jwt:SigningKeys:0:Kid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_iam_rejects_long_access_token_lifetime()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKeys:0:Kid", IamJwtTestKeys.Kid);
                builder.UseSetting("Iam:Jwt:SigningKeys:0:PrivateKeyPem", IamJwtTestKeys.PrivateKeyPem);
                builder.UseSetting("Iam:Jwt:AccessTokenMinutes", "120");
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AccessTokenMinutes", exception.Message, StringComparison.Ordinal);
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
    private sealed record ClientCredentialsTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Scope);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
    private sealed record PagedListResponse<T>(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<T> Items);
    private sealed record UserResponse(string UserId, string LoginName, string Email, bool Enabled);
    private sealed record WorkerDirectoryUserResponse(
        string UserId,
        string DisplayName,
        string? EmployeeNo,
        string? Department,
        string Status,
        string? Email);
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

    private static TokenValidationParameters CreateGatewayTokenValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = "nerv-iip-iam",
        ValidateAudience = true,
        ValidAudience = "nerv-iip-api",
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = IamJwtTestKeys.PublicJwks().Keys,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    private static void AssertAccessTokenExpiresAt(System.Security.Claims.ClaimsPrincipal principal, DateTimeOffset expiresAtUtc)
    {
        var expiresAtClaim = principal.FindFirst(JwtRegisteredClaimNames.Exp);
        Assert.NotNull(expiresAtClaim);
        Assert.Equal(expiresAtUtc.ToUnixTimeSeconds(), long.Parse(expiresAtClaim.Value));
    }

    private async Task CreateUserAsync(string loginName, string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email, password = "Operator123!" });
        response.EnsureSuccessStatusCode();
    }

    private async Task<PagedListResponse<UserResponse>> GetPagedUsersAsync(string requestUri)
    {
        var json = await _client.GetStringAsync(requestUri);
        AssertPagedEnvelope(json);
        return JsonSerializer.Deserialize<ResponseDataEnvelope<PagedListResponse<UserResponse>>>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Data!;
    }

    private async Task AssertPagedEnvelopeAsync(string requestUri)
    {
        AssertPagedEnvelope(await _client.GetStringAsync(requestUri));
    }

    private static void AssertPagedEnvelope(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("success", out var success));
        Assert.True(success.GetBoolean());
        Assert.True(document.RootElement.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("pageIndex", out _));
        Assert.True(data.TryGetProperty("pageSize", out _));
        Assert.True(data.TryGetProperty("totalCount", out _));
        Assert.True(data.TryGetProperty("items", out _));
    }

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
