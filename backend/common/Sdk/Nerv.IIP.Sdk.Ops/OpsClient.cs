using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Auth;

namespace Nerv.IIP.Sdk.Ops;

public interface IOpsClient
{
    Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default);
    Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default);
    Task<PendingOperationTasksResponse> ClaimOperationTasksAsync(ClaimOperationTasksRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> AbandonOperationTaskLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> HeartbeatOperationTaskLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, CancellationToken cancellationToken = default);
    Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default);
}

public sealed class HttpOpsClient(HttpClient httpClient, ConnectorHostCredential? credential = null) : IOpsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/operation-tasks", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "operation task", cancellationToken);
    }

    public async Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        using var response = await httpClient.GetAsync($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "operation task", cancellationToken);
    }

    public async Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ops/v1/operation-tasks/pending?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&connectorHostId={Uri.EscapeDataString(connectorHostId)}&take={take}");
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<PendingOperationTasksResponse>(response, "pending operation tasks", cancellationToken);
    }

    public async Task<PendingOperationTasksResponse> ClaimOperationTasksAsync(ClaimOperationTasksRequest claim, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ops/v1/operation-tasks/claims") { Content = JsonContent.Create(claim) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<PendingOperationTasksResponse>(response, "claim", cancellationToken);
    }

    public async Task<OperationTaskResponse> AbandonOperationTaskLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest abandon, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ops/v1/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/lease/abandon") { Content = JsonContent.Create(abandon) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "abandon", cancellationToken);
    }

    public async Task<OperationTaskResponse> HeartbeatOperationTaskLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest heartbeat, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ops/v1/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/lease/heartbeat") { Content = JsonContent.Create(heartbeat) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "heartbeat", cancellationToken);
    }

    public async Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ops/v1/operation-results") { Content = JsonContent.Create(result) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void ApplyCredential(HttpRequestMessage request)
    {
        if (credential is not null)
        {
            ConnectorHostAuthentication.Apply(request, credential);
        }
    }

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response, string responseName, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"Ops returned an empty {responseName} response.");
        }

        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.TryGetProperty("data", out var data)
            ? data
            : document.RootElement;

        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Ops returned an empty {responseName} response.");
        }

        return payload.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Ops returned an empty {responseName} response.");
    }
}
