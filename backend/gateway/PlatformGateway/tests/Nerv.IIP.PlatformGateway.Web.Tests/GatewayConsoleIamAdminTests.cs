using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleIamAdminTests
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
    public async Task Console_iam_users_requires_authentication_before_clients_are_called()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var iam = new FakeGatewayIamAuthClient();
        var admin = new FakeGatewayIamAdminClient();
        await using var factory = CreateFactory(auth, iam, admin);

        var response = await factory.CreateClient().GetAsync("/api/console/v1/iam/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Null(iam.LastMeBearerToken);
        Assert.Null(auth.LastRequirement);
        Assert.Equal(0, admin.ListUsersCallCount);
    }

    [Fact]
    public async Task Console_iam_users_returns_forbidden_when_permission_check_denies()
    {
        var auth = FakeGatewayAuthorizationClient.Forbidden();
        var iam = new FakeGatewayIamAuthClient();
        var admin = new FakeGatewayIamAdminClient();
        await using var factory = CreateFactory(auth, iam, admin);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/iam/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(client.DefaultRequestHeaders.Authorization.Parameter, iam.LastMeBearerToken);
        Assert.Equal("iam.users.read", auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal(0, admin.ListUsersCallCount);
    }

    [Fact]
    public async Task Console_iam_users_forwards_after_permission_check_and_returns_success()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var iam = new FakeGatewayIamAuthClient();
        var admin = new FakeGatewayIamAdminClient();
        await using var factory = CreateFactory(auth, iam, admin);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/iam/users?pageIndex=1&pageSize=20");
        var body = await ReadResponseDataAsync<PagedListResponse<ConsoleIamUserResponse>>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("iam.users.read", auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal(client.DefaultRequestHeaders.Authorization.Parameter, admin.LastBearerToken);
        Assert.Equal(new ConsoleIamListRequest(1, 20, null, null, null, null, null), admin.LastListUsersRequest);
        Assert.Equal(1, body.PageIndex);
        Assert.Equal(20, body.PageSize);
        Assert.Equal("user-001", body.Items.Single().UserId);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayAuthorizationClient auth,
        FakeGatewayIamAuthClient iam,
        FakeGatewayIamAdminClient admin) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayAuthorizationClient>();
            services.AddSingleton<IGatewayAuthorizationClient>(auth);
            services.RemoveAll<IGatewayIamAuthClient>();
            services.AddSingleton<IGatewayIamAuthClient>(iam);
            services.RemoveAll<IGatewayIamAdminClient>();
            services.AddSingleton<IGatewayIamAdminClient>(admin);
        }));

    private sealed class FakeGatewayIamAuthClient : IGatewayIamAuthClient
    {
        public string? LastMeBearerToken { get; private set; }

        public Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken)
        {
            LastMeBearerToken = bearerToken;
            return Task.FromResult(Principal);
        }
    }

    private sealed class FakeGatewayIamAdminClient : IGatewayIamAdminClient
    {
        public int ListUsersCallCount { get; private set; }
        public string? LastBearerToken { get; private set; }
        public ConsoleIamListRequest? LastListUsersRequest { get; private set; }

        public Task<PagedListResponse<ConsoleIamUserResponse>> ListUsersAsync(
            string bearerToken,
            ConsoleIamListRequest request,
            CancellationToken cancellationToken)
        {
            ListUsersCallCount++;
            LastBearerToken = bearerToken;
            LastListUsersRequest = request;
            return Task.FromResult(new PagedListResponse<ConsoleIamUserResponse>(
                request.PageIndex ?? 1,
                request.PageSize ?? 20,
                1,
                [new ConsoleIamUserResponse("user-001", "admin", "admin@nerv.local", true)]));
        }

        public Task<ConsoleIamUserResponse> CreateUserAsync(
            string bearerToken,
            ConsoleCreateIamUserRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsoleIamUserResponse> UpdateUserAsync(
            string bearerToken,
            string userId,
            ConsoleUpdateIamUserRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ResetUserPasswordAsync(
            string bearerToken,
            string userId,
            ConsoleResetIamUserPasswordRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedListResponse<ConsoleIamRoleResponse>> ListRolesAsync(
            string bearerToken,
            ConsoleIamListRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsoleIamRoleResponse> CreateRoleAsync(
            string bearerToken,
            ConsoleCreateIamRoleRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsoleIamRoleResponse> UpdateRolePermissionsAsync(
            string bearerToken,
            string roleId,
            ConsoleUpdateIamRolePermissionsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsoleIamPermissionCatalogResponse> ListPermissionsAsync(
            string bearerToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedListResponse<ConsoleIamSessionResponse>> ListSessionsAsync(
            string bearerToken,
            ConsoleIamListRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeSessionAsync(string bearerToken, string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

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
