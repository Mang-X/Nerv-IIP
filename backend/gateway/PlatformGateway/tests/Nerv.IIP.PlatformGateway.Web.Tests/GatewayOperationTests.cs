using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayOperationTests
{
    [Fact]
    public async Task Restart_endpoint_creates_lifecycle_restart_task()
    {
        var fake = new FakeGatewayOpsClient();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayOpsClient>();
                services.AddSingleton<IGatewayOpsClient>(fake);
            }));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/console/v1/instances/docker-container-local-demo-001/operations/restart", new RestartInstanceRequest("org-001", "env-dev", "smoke restart", "idem-gateway-restart-001"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OperationTaskResponse>();
        Assert.NotNull(body);
        Assert.Equal("docker-container-local-demo-001", body.InstanceKey);
        Assert.Equal("lifecycle.restart", body.OperationCode);
        Assert.Equal("queued", body.Status);
        Assert.Equal("idem-gateway-restart-001", fake.LastRequest!.IdempotencyKey);
        Assert.Equal("smoke restart", fake.LastRequest.Reason);
    }

    [Fact]
    public async Task Restart_endpoint_returns_bad_gateway_when_ops_is_unavailable()
    {
        var fake = new FakeGatewayOpsClient { CreateFailure = new HttpRequestException("Ops down") };
        await using var factory = CreateFactory(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/console/v1/instances/docker-container-local-demo-001/operations/restart", new RestartInstanceRequest("org-001", "env-dev", "smoke restart", "idem-gateway-restart-001"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Ops unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Operation_detail_endpoint_returns_operation_task()
    {
        var fake = new FakeGatewayOpsClient();
        await using var factory = CreateFactory(fake);
        var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<OperationTaskResponse>("/api/console/v1/operation-tasks/op-000001");

        Assert.NotNull(response);
        Assert.Equal("op-000001", response.OperationTaskId);
        Assert.Equal("completed", response.Status);
        Assert.Equal("op-000001", fake.LastOperationTaskId);
    }

    [Fact]
    public async Task Operation_detail_endpoint_returns_bad_gateway_when_ops_is_unavailable()
    {
        var fake = new FakeGatewayOpsClient { GetFailure = new HttpRequestException("Ops down") };
        await using var factory = CreateFactory(fake);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/console/v1/operation-tasks/op-000001");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Ops unavailable", await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeGatewayOpsClient fake)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayOpsClient>();
                services.AddSingleton<IGatewayOpsClient>(fake);
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
}
