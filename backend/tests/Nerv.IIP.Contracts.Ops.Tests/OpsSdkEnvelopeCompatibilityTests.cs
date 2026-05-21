using System.Net;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.Contracts.Ops.Tests;

public sealed class OpsSdkEnvelopeCompatibilityTests
{
    [Fact]
    public async Task CreateOperationTaskAsync_ResponseDataEnvelope_ReturnsData()
    {
        var expected = CreateOperationTaskResponse("op-create");
        using var httpClient = CreateHttpClient(_ => JsonResponse(new ResponseDataEnvelope<OperationTaskResponse>(expected)));
        var client = new HttpOpsClient(httpClient);

        var result = await client.CreateOperationTaskAsync(new CreateOperationTaskRequest(
            "org-001",
            "env-dev",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "idem-001",
            "local-admin",
            "restart for readiness",
            "corr-001",
            new Dictionary<string, string>()));

        Assert.Equal("op-create", result.OperationTaskId);
    }

    [Fact]
    public async Task GetOperationTaskAsync_ResponseDataEnvelope_ReturnsData()
    {
        var expected = CreateOperationTaskResponse("op-get");
        using var httpClient = CreateHttpClient(_ => JsonResponse(new ResponseDataEnvelope<OperationTaskResponse>(expected)));
        var client = new HttpOpsClient(httpClient);

        var result = await client.GetOperationTaskAsync("op-get");

        Assert.Equal("op-get", result.OperationTaskId);
    }

    [Fact]
    public async Task GetPendingOperationTasksAsync_ResponseDataEnvelope_ReturnsData()
    {
        var expected = new PendingOperationTasksResponse([CreateDispatchItem("op-pending")]);
        using var httpClient = CreateHttpClient(_ => JsonResponse(new ResponseDataEnvelope<PendingOperationTasksResponse>(expected)));
        var client = new HttpOpsClient(httpClient);

        var result = await client.GetPendingOperationTasksAsync("org-001", "env-dev", "connector-host-001", 5);

        Assert.Equal("op-pending", result.Items.Single().OperationTaskId);
    }

    [Fact]
    public async Task ClaimOperationTasksAsync_ResponseDataEnvelope_ReturnsData()
    {
        var expected = new PendingOperationTasksResponse([CreateDispatchItem("op-claim")]);
        using var httpClient = CreateHttpClient(_ => JsonResponse(new ResponseDataEnvelope<PendingOperationTasksResponse>(expected)));
        var client = new HttpOpsClient(httpClient);

        var result = await client.ClaimOperationTasksAsync(new ClaimOperationTasksRequest(
            "org-001",
            "env-dev",
            "connector-host-001",
            1));

        Assert.Equal("op-claim", result.Items.Single().OperationTaskId);
    }

    [Fact]
    public async Task AbandonOperationTaskLeaseAsync_ResponseDataEnvelope_ReturnsData()
    {
        var expected = CreateOperationTaskResponse("op-abandon");
        using var httpClient = CreateHttpClient(_ => JsonResponse(new ResponseDataEnvelope<OperationTaskResponse>(expected)));
        var client = new HttpOpsClient(httpClient);

        var result = await client.AbandonOperationTaskLeaseAsync("op-abandon", new AbandonOperationTaskLeaseRequest(
            "org-001",
            "env-dev",
            "connector-host-001",
            "lease-001",
            "worker shutdown"));

        Assert.Equal("op-abandon", result.OperationTaskId);
    }

    [Fact]
    public async Task HeartbeatOperationTaskLeaseAsync_ResponseDataEnvelope_ReturnsData()
    {
        var expected = CreateOperationTaskResponse("op-heartbeat");
        using var httpClient = CreateHttpClient(_ => JsonResponse(new ResponseDataEnvelope<OperationTaskResponse>(expected)));
        var client = new HttpOpsClient(httpClient);

        var result = await client.HeartbeatOperationTaskLeaseAsync("op-heartbeat", new HeartbeatOperationTaskLeaseRequest(
            "org-001",
            "env-dev",
            "connector-host-001",
            "lease-001"));

        Assert.Equal("op-heartbeat", result.OperationTaskId);
    }

    [Fact]
    public async Task SendOperationResultAsync_EmptySuccessResponse_Completes()
    {
        using var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpOpsClient(httpClient);

        await client.SendOperationResultAsync(CreateOperationResult("op-result"));
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        return new HttpClient(new StubHttpMessageHandler(respond))
        {
            BaseAddress = new Uri("https://ops.example.test")
        };
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };
    }

    private static OperationTaskResponse CreateOperationTaskResponse(string operationTaskId)
    {
        return new OperationTaskResponse(
            operationTaskId,
            "org-001",
            "env-dev",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "pending",
            "local-admin",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            null,
            [],
            []);
    }

    private static OperationTaskDispatchItem CreateDispatchItem(string operationTaskId)
    {
        return new OperationTaskDispatchItem(
            operationTaskId,
            "attempt-001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "corr-001",
            new Dictionary<string, string>(),
            "lease-001",
            DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
            DateTimeOffset.Parse("2026-05-15T00:05:01Z"),
            1,
            3);
    }

    private static OperationResult CreateOperationResult(string operationTaskId)
    {
        return new OperationResult(
            new ConnectorRequestContext(
                "1.0",
                "1.0",
                "corr-001",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
                "org-001",
                "env-dev",
                "connector-host-001"),
            operationTaskId,
            "attempt-001",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "succeeded",
            null,
            new Dictionary<string, string>());
    }

    private sealed record ResponseDataEnvelope<T>(T Data, bool Success = true, string Message = "OK", int Code = 200);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
