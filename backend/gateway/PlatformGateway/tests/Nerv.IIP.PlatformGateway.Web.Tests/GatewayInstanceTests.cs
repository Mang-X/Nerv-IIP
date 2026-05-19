using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

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

        var list = await client.GetFromJsonAsync<InstanceListResponse>("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev&pageNumber=1&pageSize=20&search=demo");
        var detail = await client.GetFromJsonAsync<InstanceDetailResponse>("/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");
        fake.Detail = fake.Detail! with { ReportedStatus = "stopped" };
        var cached = await client.GetFromJsonAsync<InstanceDetailResponse>("/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");
        await client.PostAsync("/internal/gateway/cache/invalidate", null);
        var refreshed = await client.GetFromJsonAsync<InstanceDetailResponse>("/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(new InstanceListQuery("org-001", "env-dev", 1, 20, "demo"), fake.LastQuery);
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

        var response = await client.GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev&pageNumber=1&pageSize=20");

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
            return Task.FromResult(new InstanceListResponse(query.PageNumber, query.PageSize, 1, [new InstanceListItem("demo-api", "Demo API", "1.0.0", "node-001", "local-docker", "demo-api-001", "demo-api", "running", "healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]));
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
}
