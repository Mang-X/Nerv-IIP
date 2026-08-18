using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Xunit;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

/// <summary>
/// Behavioural proof that parallel PlatformGateway hosts keep their observable request state apart
/// and never mutate FastEndpoints process state underneath an in-flight request.
/// </summary>
public sealed class PlatformGatewayHostIsolationTests
{
    private const string InstancesRoute =
        "/api/console/v1/instances?organizationId=org-001&environmentId=env-dev&pageIndex=1&pageSize=20";

    private static readonly TimeSpan BuildMustStayBlockedFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BuildMustCompleteWithin = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Parallel_hosts_never_observe_another_hosts_authorization_or_downstream_fake()
    {
        var allowedAppHub = new RecordingAppHubClient("allowed-instance");
        var deniedAppHub = new RecordingAppHubClient("denied-instance");
        await using var allowedFactory = CreateFactory(FakeGatewayAuthorizationClient.Allowed(), allowedAppHub);
        await using var deniedFactory = CreateFactory(FakeGatewayAuthorizationClient.Forbidden(), deniedAppHub);

        using var allowedClient = Authenticated(allowedFactory.CreateClient());
        using var deniedClient = Authenticated(deniedFactory.CreateClient());

        var responses = await Task.WhenAll(
            allowedClient.GetAsync(InstancesRoute),
            deniedClient.GetAsync(InstancesRoute),
            allowedClient.GetAsync(InstancesRoute));

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[2].StatusCode);
        Assert.InRange(allowedAppHub.QueryCallCount, 1, 2);
        Assert.Equal(0, deniedAppHub.QueryCallCount);
        Assert.All(
            new[] { await responses[0].Content.ReadAsStringAsync(), await responses[2].Content.ReadAsStringAsync() },
            body => Assert.Contains("allowed-instance", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_construction_waits_for_a_response_body_that_is_still_being_written()
    {
        await using var servingFactory = PlatformGatewayTestHost.CreateFactory();
        using var client = servingFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var buildingFactory = PlatformGatewayTestHost.CreateFactory();
        var build = Task.Run(() => buildingFactory.Services);
        var completedTooEarly = true;
        try
        {
            await build.WaitAsync(BuildMustStayBlockedFor);
        }
        catch (TimeoutException)
        {
            completedTooEarly = false;
        }

        Assert.False(
            completedTooEarly,
            "PlatformGateway host construction completed while another host was still writing a response body "
            + $"(gate reported {PlatformGatewayTestHostGate.RequestsInFlight} request(s) in flight).");

        var document = await response.Content.ReadAsStringAsync();
        Assert.True(
            document.Length > 64 * 1024,
            $"The PlatformGateway OpenAPI document is only {document.Length} bytes, so the response no longer "
            + "exceeds TestServer's response-pipe threshold and this test would not observe the guarded window.");

        await build.WaitAsync(BuildMustCompleteWithin);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        IGatewayAuthorizationClient authorization,
        IAppHubClient appHub) =>
        PlatformGatewayTestHost.CreateFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton(authorization);
                services.RemoveAll<IAppHubClient>();
                services.AddSingleton(appHub);
            }));

    private static HttpClient Authenticated(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        return client;
    }

    private sealed class RecordingAppHubClient(string instanceKey) : IAppHubClient
    {
        private int _queryCallCount;

        public int QueryCallCount => Volatile.Read(ref _queryCallCount);

        public Task<InstanceListResponse> QueryInstancesAsync(
            InstanceListQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _queryCallCount);
            return Task.FromResult(new InstanceListResponse(
                query.PageIndex,
                query.PageSize,
                1,
                [new InstanceListItem(
                    "demo-api",
                    "Demo API",
                    "1.0.0",
                    "node-001",
                    "local-docker",
                    instanceKey,
                    "demo-api",
                    "running",
                    "healthy",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)]));
        }

        public Task<InstanceDetailResponse> GetInstanceAsync(
            string organizationId,
            string environmentId,
            string requestedInstanceKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
