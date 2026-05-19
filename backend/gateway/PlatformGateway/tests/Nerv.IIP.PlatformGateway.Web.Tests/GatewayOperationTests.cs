using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayOperationTests
{
    [Fact]
    public async Task Restart_endpoint_creates_lifecycle_restart_task()
    {
        var fake = new FakeGatewayOpsClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(fake, auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/console/v1/instances/docker-container-local-demo-001/operations/restart", new RestartInstanceRequest("org-001", "env-dev", "smoke restart", "idem-gateway-restart-001"));

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<OperationTaskResponse>(response);
        Assert.NotNull(body);
        Assert.Equal("docker-container-local-demo-001", body.InstanceKey);
        Assert.Equal("lifecycle.restart", body.OperationCode);
        Assert.Equal("queued", body.Status);
        Assert.Equal("idem-gateway-restart-001", fake.LastRequest!.IdempotencyKey);
        Assert.Equal("smoke restart", fake.LastRequest.Reason);
        Assert.Equal("user-admin", fake.LastRequest.RequestedBy);
        Assert.Equal(GatewayPermissions.OpsTasksCreate, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    [Fact]
    public async Task Restart_endpoint_returns_bad_gateway_when_ops_is_unavailable()
    {
        var fake = new FakeGatewayOpsClient { CreateFailure = new HttpRequestException("Ops down") };
        await using var factory = CreateFactory(fake);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/console/v1/instances/docker-container-local-demo-001/operations/restart", new RestartInstanceRequest("org-001", "env-dev", "smoke restart", "idem-gateway-restart-001"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Ops unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Operation_detail_endpoint_returns_operation_task()
    {
        var fake = new FakeGatewayOpsClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(fake, auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var httpResponse = await client.GetAsync("/api/console/v1/operation-tasks/op-000001?organizationId=org-001&environmentId=env-dev");
        httpResponse.EnsureSuccessStatusCode();
        var response = await ReadResponseDataAsync<OperationTaskResponse>(httpResponse);

        Assert.NotNull(response);
        Assert.Equal("op-000001", response.OperationTaskId);
        Assert.Equal("completed", response.Status);
        Assert.Equal("op-000001", fake.LastOperationTaskId);
        Assert.Equal(GatewayPermissions.OpsTasksRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Operation_detail_endpoint_returns_bad_gateway_when_ops_is_unavailable()
    {
        var fake = new FakeGatewayOpsClient { GetFailure = new HttpRequestException("Ops down") };
        await using var factory = CreateFactory(fake);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/operation-tasks/op-000001?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Ops unavailable", await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayOpsClient fake,
        FakeGatewayAuthorizationClient? auth = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayOpsClient>();
                services.AddSingleton<IGatewayOpsClient>(fake);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth ?? FakeGatewayAuthorizationClient.Allowed());
            }));
    }

    private sealed class FakeGatewayOpsClient : IGatewayOpsClient
    {
        public CreateOperationTaskRequest? LastRequest { get; private set; }
        public string? LastOperationTaskId { get; private set; }
        public HttpRequestException? CreateFailure { get; init; }
        public HttpRequestException? GetFailure { get; init; }

        public Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
        {
            if (CreateFailure is not null)
            {
                throw CreateFailure;
            }

            LastRequest = request;
            return Task.FromResult(new OperationTaskResponse("op-000001", request.OrganizationId, request.EnvironmentId, request.InstanceKey, request.OperationCode, "queued", request.RequestedBy, DateTimeOffset.UtcNow, null, [], []));
        }

        public Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken)
        {
            if (GetFailure is not null)
            {
                throw GetFailure;
            }

            LastOperationTaskId = operationTaskId;
            return Task.FromResult(new OperationTaskResponse(operationTaskId, "org-001", "env-dev", "docker-container-local-demo-001", "lifecycle.restart", "completed", "local-admin", DateTimeOffset.UtcNow, "attempt-000001", [], []));
        }
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
