using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleAuthTests
{
    private static readonly ConsolePrincipalResponse Principal = new(
        "user-admin",
        "user",
        "admin",
        "admin@nerv.local",
        "org-001",
        "env-dev",
        7);

    [Fact]
    public async Task Console_login_forwards_to_iam_and_returns_session_payload()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/login",
            new ConsoleLoginRequest("admin", "secret"));
        var body = await response.Content.ReadFromJsonAsync<ConsoleAuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ConsoleLoginRequest("admin", "secret"), iam.LastLoginRequest);
        Assert.Equal("access-token", body!.AccessToken);
        Assert.Equal("refresh-token", body.RefreshToken);
        Assert.Equal("session-001", body.SessionId);
        Assert.Equal(Principal, body.Principal);
    }

    [Fact]
    public async Task Console_login_maps_invalid_credentials_to_unauthorized()
    {
        var iam = new FakeGatewayIamAuthClient
        {
            ExceptionToThrow = GatewayAuthException.Unauthorized("invalid-credentials")
        };
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/login",
            new ConsoleLoginRequest("admin", "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid-credentials", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Console_refresh_forwards_refresh_token()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/refresh",
            new ConsoleRefreshRequest("refresh-token"));
        var body = await response.Content.ReadFromJsonAsync<ConsoleAuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ConsoleRefreshRequest("refresh-token"), iam.LastRefreshRequest);
        Assert.Equal("access-token", body!.AccessToken);
        Assert.Equal(Principal, body.Principal);
    }

    [Fact]
    public async Task Console_logout_forwards_bearer_and_session()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "access-token");

        var response = await client.PostAsJsonAsync(
            "/api/console/v1/auth/logout",
            new ConsoleLogoutRequest("session-001"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("access-token", iam.LastLogoutBearerToken);
        Assert.Equal(new ConsoleLogoutRequest("session-001"), iam.LastLogoutRequest);
    }

    [Fact]
    public async Task Console_logout_requires_bearer()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/logout",
            new ConsoleLogoutRequest("session-001"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(iam.LastLogoutBearerToken);
        Assert.Null(iam.LastLogoutRequest);
    }

    [Fact]
    public async Task Console_me_forwards_bearer()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "access-token");

        var body = await client.GetFromJsonAsync<ConsolePrincipalResponse>("/api/console/v1/auth/me");

        Assert.Equal("access-token", iam.LastMeBearerToken);
        Assert.Equal(Principal, body);
    }

    [Fact]
    public async Task Console_me_requires_bearer()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().GetAsync("/api/console/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(iam.LastMeBearerToken);
    }

    [Fact]
    public async Task Console_auth_maps_iam_unavailable_to_service_unavailable()
    {
        var iam = new FakeGatewayIamAuthClient
        {
            ExceptionToThrow = GatewayAuthException.Unavailable("iam-unavailable")
        };
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/login",
            new ConsoleLoginRequest("admin", "secret"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("iam-unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Iam_auth_client_maps_malformed_success_body_to_bad_gateway()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json")
            }))
        {
            BaseAddress = new Uri("http://iam.local")
        };
        var iam = new HttpGatewayIamAuthClient(httpClient);

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(() =>
            iam.LoginAsync(new ConsoleLoginRequest("admin", "secret"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("iam-invalid-response", exception.Reason);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeGatewayIamAuthClient iam) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayIamAuthClient>();
            services.AddSingleton<IGatewayIamAuthClient>(iam);
        }));

    private sealed class FakeGatewayIamAuthClient : IGatewayIamAuthClient
    {
        public ConsoleLoginRequest? LastLoginRequest { get; private set; }
        public ConsoleRefreshRequest? LastRefreshRequest { get; private set; }
        public ConsoleLogoutRequest? LastLogoutRequest { get; private set; }
        public string? LastLogoutBearerToken { get; private set; }
        public string? LastMeBearerToken { get; private set; }
        public GatewayAuthException? ExceptionToThrow { get; init; }

        public Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastLoginRequest = request;
            return Task.FromResult(Session());
        }

        public Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRefreshRequest = request;
            return Task.FromResult(Session());
        }

        public Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastLogoutBearerToken = bearerToken;
            LastLogoutRequest = request;
            return Task.CompletedTask;
        }

        public Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastMeBearerToken = bearerToken;
            return Task.FromResult(Principal);
        }

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }

        private static ConsoleAuthResponse Session() => new(
            "access-token",
            "refresh-token",
            "session-001",
            DateTimeOffset.UtcNow.AddMinutes(15),
            Principal);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
