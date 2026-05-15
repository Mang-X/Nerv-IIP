using System.Net.Http.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Auth;

namespace Nerv.IIP.Sdk.Ops;

public interface IOpsClient
{
    Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default);
    Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default);
    Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default);
}

public sealed class HttpOpsClient(HttpClient httpClient, ConnectorHostCredential? credential = null) : IOpsClient
{
    public async Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/operation-tasks", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OperationTaskResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty operation task response.");
    }

    public async Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        return await httpClient.GetFromJsonAsync<OperationTaskResponse>($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}", cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty operation task response.");
    }

    public async Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ops/v1/operation-tasks/pending?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&connectorHostId={Uri.EscapeDataString(connectorHostId)}&take={take}");
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PendingOperationTasksResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty pending operation tasks response.");
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
}
