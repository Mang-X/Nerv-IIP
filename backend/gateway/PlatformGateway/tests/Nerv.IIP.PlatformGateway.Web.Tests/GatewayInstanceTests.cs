using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayInstanceTests
{
    [Fact]
    public async Task Instance_endpoints_map_query_cache_detail_and_do_not_reference_apphub_implementation()
    {
        var fake = new FakeAppHubClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAppHubClient>();
                services.AddSingleton<IAppHubClient>(fake);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth);
            }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var listResponse = await client.GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev&pageIndex=1&pageSize=20&sortBy=instanceName&sortOrder=asc&filterSearch=demo");
        listResponse.EnsureSuccessStatusCode();
        var list = await ReadResponseDataAsync<InstanceListResponse>(listResponse);
        var detailResponse = await client.GetAsync("/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");
        detailResponse.EnsureSuccessStatusCode();
        var detail = await ReadResponseDataAsync<InstanceDetailResponse>(detailResponse);
        fake.Detail = fake.Detail! with { ReportedStatus = "stopped" };
        var cachedResponse = await client.GetAsync("/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");
        cachedResponse.EnsureSuccessStatusCode();
        var cached = await ReadResponseDataAsync<InstanceDetailResponse>(cachedResponse);
        using var invalidateRequest = new HttpRequestMessage(HttpMethod.Post, "/internal/gateway/cache/invalidate");
        invalidateRequest.Headers.Authorization = new("Bearer", InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        var invalidateResponse = await client.SendAsync(invalidateRequest);
        invalidateResponse.EnsureSuccessStatusCode();
        var refreshedResponse = await client.GetAsync("/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");
        refreshedResponse.EnsureSuccessStatusCode();
        var refreshed = await ReadResponseDataAsync<InstanceDetailResponse>(refreshedResponse);

        Assert.Equal(new InstanceListQuery("org-001", "env-dev", 1, 20, "instanceName", "asc", "demo"), fake.LastQuery);
        Assert.Equal(1, list!.PageIndex);
        Assert.Equal(20, list.PageSize);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(GatewayPermissions.AppHubInstancesRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal("demo-api-001", detail!.InstanceKey);
        Assert.Equal("running", cached!.ReportedStatus);
        Assert.Equal("stopped", refreshed!.ReportedStatus);
        var projectFile = FindGatewayProjectFile();
        Assert.DoesNotContain("Nerv.IIP.AppHub.Domain", File.ReadAllText(projectFile));
        Assert.DoesNotContain("Nerv.IIP.AppHub.Infrastructure", File.ReadAllText(projectFile));
    }

    [Fact]
    public async Task Instance_endpoint_returns_diagnostic_failure_when_apphub_is_unavailable()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAppHubClient>();
                services.AddSingleton<IAppHubClient>(new FailingAppHubClient());
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth);
            }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev&pageIndex=1&pageSize=20");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("AppHub unavailable", await response.Content.ReadAsStringAsync());
    }

    private sealed class FakeAppHubClient : IAppHubClient
    {
        public InstanceListQuery? LastQuery { get; private set; }
        public InstanceDetailResponse? Detail { get; set; } = new("demo-api", "Demo API", "1.0.0", "node-001", "local-docker", "demo-api-001", "demo-api", "running", "healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [new CapabilitySummary("lifecycle.restart", "1.0", "lifecycle", ["restart"])], new Dictionary<string, string>());

        public Task<InstanceListResponse> QueryInstancesAsync(InstanceListQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new InstanceListResponse(query.PageIndex, query.PageSize, 1, [new InstanceListItem("demo-api", "Demo API", "1.0.0", "node-001", "local-docker", "demo-api-001", "demo-api", "running", "healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]));
        }

        public Task<InstanceDetailResponse> GetInstanceAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken) => Task.FromResult(Detail!);
    }

    private sealed class FailingAppHubClient : IAppHubClient
    {
        public Task<InstanceListResponse> QueryInstancesAsync(InstanceListQuery query, CancellationToken cancellationToken) => throw new HttpRequestException("AppHub down");
        public Task<InstanceDetailResponse> GetInstanceAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken) => throw new HttpRequestException("AppHub down");
    }

    private static string FindGatewayProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Nerv.IIP.PlatformGateway.Web", "Nerv.IIP.PlatformGateway.Web.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Gateway project file was not found.");
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
