using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamUserLifecyclePolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public IamUserLifecyclePolicyTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Disabling_user_revokes_active_sessions_refresh_tokens_and_new_logins()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var loginName = $"disabled-{suffix}";
        var password = "Disabled123!";
        var user = await CreateUserAsync(loginName, $"{loginName}@nerv-iip.local", password);
        await ChangePasswordAsync(loginName, password, "Enabled123!");

        var login = await LoginAsync(loginName, "Enabled123!");

        var disable = await _client.PostAsync($"/api/iam/v1/users/{user.UserId}/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var staleBearer = await _client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, staleBearer.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;

        var refresh = await _client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        var sessions = await GetPagedAsync<SessionResponse>(
            $"/api/iam/v1/sessions?filterSearch={user.UserId}&filterRevoked=false&pageIndex=1&pageSize=20");
        Assert.DoesNotContain(sessions.Items, session => session.UserId == user.UserId);

        var disabledLogin = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName, password = "Enabled123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, disabledLogin.StatusCode);
    }

    [Fact]
    public async Task Expired_account_is_rejected_for_login()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var loginName = $"expired-{suffix}";
        var create = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new
            {
                loginName,
                email = $"{loginName}@nerv-iip.local",
                password = "Expired123!",
                accountExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var login = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/login",
            new { loginName, password = "Expired123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Password_policy_rejects_weak_passwords_and_history_reuse()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var loginName = $"policy-{suffix}";
        var weakCreate = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email = $"{loginName}@nerv-iip.local", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weakCreate.StatusCode);

        await CreateUserAsync(loginName, $"{loginName}@nerv-iip.local", "Policy123!");
        await ChangePasswordAsync(loginName, "Policy123!", "Policy234!");

        var login = await LoginAsync(loginName, "Policy234!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var reuse = await _client.PostAsJsonAsync(
            "/api/iam/v1/auth/change-password",
            new { currentPassword = "Policy234!", newPassword = "Policy123!" });
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task First_login_and_admin_reset_require_password_change_until_self_service_change()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var loginName = $"force-change-{suffix}";
        var user = await CreateUserAsync(loginName, $"{loginName}@nerv-iip.local", "Initial123!");

        var firstLogin = await LoginAsync(loginName, "Initial123!");
        Assert.True(firstLogin.PasswordChangeRequired);

        await ChangePasswordAsync(loginName, "Initial123!", "Changed123!");
        var afterChange = await LoginAsync(loginName, "Changed123!");
        Assert.False(afterChange.PasswordChangeRequired);

        var reset = await _client.PostAsJsonAsync(
            $"/api/iam/v1/users/{user.UserId}/reset-password",
            new { newPassword = "Reset123!" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var resetLogin = await LoginAsync(loginName, "Reset123!");
        Assert.True(resetLogin.PasswordChangeRequired);
    }

    [Fact]
    public async Task InMemory_password_policy_uses_configured_options()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:PasswordPolicy:MinimumLength", "12");
            builder.UseSetting("Iam:PasswordPolicy:RequireNonAlphanumeric", "false");
            builder.UseSetting("Iam:PasswordPolicy:PasswordExpiresDays", "0");
            builder.UseSetting("Iam:PasswordPolicy:PasswordHistoryCount", "1");
        });
        using var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var loginName = $"configured-policy-{suffix}";

        var tooShort = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email = $"{loginName}@nerv-iip.local", password = "Short123" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var create = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email = $"{loginName}@nerv-iip.local", password = "LongPassword123" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var user = await ReadResponseDataAsync<UserResponse>(create);
        Assert.Null(user.PasswordExpiresAtUtc);

        await ChangePasswordAsync(client, loginName, "LongPassword123", "LongPassword234");
        await ChangePasswordAsync(client, loginName, "LongPassword234", "LongPassword345");
        await ChangePasswordAsync(client, loginName, "LongPassword345", "LongPassword123");
    }

    private async Task<UserResponse> CreateUserAsync(string loginName, string email, string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email, password });
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<UserResponse>(response);
    }

    private async Task<AuthResponse> LoginAsync(string loginName, string password)
    {
        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName, password });
        login.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<AuthResponse>(login);
    }

    private async Task ChangePasswordAsync(string loginName, string currentPassword, string newPassword)
    {
        await ChangePasswordAsync(_client, loginName, currentPassword, newPassword);
    }

    private static async Task ChangePasswordAsync(
        HttpClient client,
        string loginName,
        string currentPassword,
        string newPassword)
    {
        var login = await LoginAsync(client, loginName, currentPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var change = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/change-password",
            new { currentPassword, newPassword });
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
    }

    private static async Task<AuthResponse> LoginAsync(HttpClient client, string loginName, string password)
    {
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName, password });
        login.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<AuthResponse>(login);
    }

    private async Task<PagedListResponse<T>> GetPagedAsync<T>(string requestUri)
    {
        var response = await _client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<PagedListResponse<T>>(response);
    }

    private sealed record AuthResponse(
        string AccessToken,
        string RefreshToken,
        string SessionId,
        DateTimeOffset ExpiresAtUtc,
        bool PasswordChangeRequired);

    private sealed record UserResponse(
        string UserId,
        string LoginName,
        string Email,
        bool Enabled,
        DateTimeOffset? AccountExpiresAtUtc,
        bool PasswordChangeRequired,
        DateTimeOffset? PasswordExpiresAtUtc,
        DateTimeOffset? LockoutUntilUtc);

    private sealed record SessionResponse(
        string SessionId,
        string UserId,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? RevokedAtUtc,
        int PermissionVersion);

    private sealed record PagedListResponse<T>(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<T> Items);
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
