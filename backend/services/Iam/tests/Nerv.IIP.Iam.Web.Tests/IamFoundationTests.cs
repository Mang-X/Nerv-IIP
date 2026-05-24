using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public IamFoundationTests(WebApplicationFactory<Program> factory)
    {
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
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.work-orders.read"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.work-orders.manage"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.reporting.write"
            && item.Domain == "business"
            && item.Seeded);
        Assert.Contains(catalog.Items, item => item.Code == "business.mes.schedules.manage"
            && item.Domain == "business"
            && item.Seeded);

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
    public void In_memory_access_token_validation_rejects_expired_token_payload()
    {
        var store = new InMemoryIamStore();
        var auth = store.Login("admin", "Admin123!");
        var user = Assert.Single(store.Users);
        var expiredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var expiredToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{auth.SessionId}|{user.SecurityStamp}|{user.PermissionVersion}|{expiredAtUtc}"));

        var exception = Assert.Throws<UnauthorizedAccessException>(() => store.ValidateAccessToken(expiredToken));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
    private sealed record PagedListResponse<T>(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<T> Items);
    private sealed record UserResponse(string UserId, string LoginName, string Email, bool Enabled);
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
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("nerv-iip-iam-development-signing-key-local-only-0001")),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

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
