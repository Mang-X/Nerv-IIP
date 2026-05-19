using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));

        var refresh = await _client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await refresh.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEqual(auth.RefreshToken, rotated!.RefreshToken);

        var oldRefresh = await _client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

        var connector = await _client.PostAsJsonAsync("/api/iam/v1/connectors/credentials/validate", new { connectorHostId = "connector-host-001", secret = "local-connector-secret" });
        connector.EnsureSuccessStatusCode();
        var connectorPrincipal = await connector.Content.ReadFromJsonAsync<ConnectorPrincipalResponse>();
        Assert.Equal("connector-host", connectorPrincipal!.PrincipalType);
        Assert.Equal("org-001", connectorPrincipal.OrganizationId);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        var meBeforeLogout = await _client.GetAsync("/api/iam/v1/me");
        meBeforeLogout.EnsureSuccessStatusCode();
        var principal = await meBeforeLogout.Content.ReadFromJsonAsync<MeResponse>();

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
    public async Task In_memory_user_management_creates_updates_and_disables_users()
    {
        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName = "operator", email = "operator@nerv-iip.local", password = "Operator123!" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<UserResponse>();

        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.UserId));
        Assert.Equal("operator", created.LoginName);
        Assert.Equal("operator@nerv-iip.local", created.Email);
        Assert.True(created.Enabled);

        var patch = await _client.PatchAsJsonAsync(
            $"/api/iam/v1/users/{created.UserId}",
            new { loginName = "operator-updated", email = "operator.updated@nerv-iip.local", enabled = true });
        patch.EnsureSuccessStatusCode();
        var updated = await patch.Content.ReadFromJsonAsync<UserResponse>();

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
    private sealed record PagedListResponse<T>(int TotalCount, int PageIndex, int PageSize, IReadOnlyList<T> Items);
    private sealed record UserResponse(string UserId, string LoginName, string Email, bool Enabled);
    private sealed record MeResponse(
        string UserId,
        string LoginName,
        string Email,
        string PrincipalType,
        string OrganizationId,
        string EnvironmentId,
        int PermissionVersion);
    private sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);

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
        return JsonSerializer.Deserialize<PagedListResponse<UserResponse>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private async Task AssertPagedEnvelopeAsync(string requestUri)
    {
        AssertPagedEnvelope(await _client.GetStringAsync(requestUri));
    }

    private static void AssertPagedEnvelope(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("totalCount", out _));
        Assert.True(document.RootElement.TryGetProperty("pageIndex", out _));
        Assert.True(document.RootElement.TryGetProperty("pageSize", out _));
        Assert.True(document.RootElement.TryGetProperty("items", out _));
    }
}
