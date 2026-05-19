using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayAuthorizationTests
{
    [Fact]
    public async Task Console_instances_require_bearer_token()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var appHub = new FakeAppHubClient();
        await using var factory = CreateFactory(auth, appHub);

        var response = await factory.CreateClient().GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Null(auth.LastRequirement);
        Assert.Equal(0, appHub.QueryCallCount);
    }

    [Fact]
    public async Task Console_instances_reject_invalid_bearer_before_permission_check()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var appHub = new FakeAppHubClient();
        await using var factory = CreateFactory(auth, appHub);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "abc.def.ghi");

        var response = await client.GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Null(auth.LastRequirement);
        Assert.Equal(0, appHub.QueryCallCount);
    }

    [Fact]
    public async Task Console_instances_return_forbidden_when_iam_denies_permission()
    {
        var auth = FakeGatewayAuthorizationClient.Forbidden();
        var appHub = new FakeAppHubClient();
        await using var factory = CreateFactory(auth, appHub);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("apphub.instances.read", auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal(0, appHub.QueryCallCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayAuthorizationClient auth,
        FakeAppHubClient appHub) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayAuthorizationClient>();
            services.AddSingleton<IGatewayAuthorizationClient>(auth);
            services.RemoveAll<IAppHubClient>();
            services.AddSingleton<IAppHubClient>(appHub);
        }));

    private sealed class FakeAppHubClient : IAppHubClient
    {
        public int QueryCallCount { get; private set; }

        public Task<InstanceListResponse> QueryInstancesAsync(InstanceListQuery query, CancellationToken cancellationToken)
        {
            QueryCallCount++;
            return Task.FromResult(new InstanceListResponse(query.PageNumber, query.PageSize, 0, []));
        }

        public Task<InstanceDetailResponse> GetInstanceAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}

internal sealed class FakeGatewayAuthorizationClient(bool allowed) : IGatewayAuthorizationClient
{
    public GatewayPermissionRequirement? LastRequirement { get; private set; }

    public static FakeGatewayAuthorizationClient Allowed() => new(true);

    public static FakeGatewayAuthorizationClient Forbidden() => new(false);

    public Task<GatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        LastRequirement = requirement;
        return Task.FromResult(allowed
            ? GatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
            : GatewayAuthorizationResult.Forbidden("forbidden"));
    }
}
