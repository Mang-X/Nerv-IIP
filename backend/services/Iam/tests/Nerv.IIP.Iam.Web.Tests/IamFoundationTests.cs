using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
        var principal = await connector.Content.ReadFromJsonAsync<ConnectorPrincipalResponse>();
        Assert.Equal("connector-host", principal!.PrincipalType);
        Assert.Equal("org-001", principal.OrganizationId);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        var logout = await _client.PostAsJsonAsync("/api/iam/v1/auth/logout", new { sessionId = rotated.SessionId });
        logout.EnsureSuccessStatusCode();

        var me = await _client.GetAsync("/api/iam/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId);
    private sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
}
