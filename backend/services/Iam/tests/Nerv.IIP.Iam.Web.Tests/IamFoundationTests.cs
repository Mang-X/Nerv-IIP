using System.Net;
using System.Net.Http.Json;
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

        var usersResponse = await _client.GetAsync("/api/iam/v1/users");
        usersResponse.EnsureSuccessStatusCode();
        var users = await ReadResponseDataAsync<UserResponse[]>(usersResponse);
        var disabled = Assert.Single(users!, user => user.UserId == created.UserId);
        Assert.False(disabled.Enabled);
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
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
