using System.Net.Http.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Auth;
using Nerv.IIP.Sdk.Core;

namespace Nerv.IIP.Sdk.Ops;

public interface IOpsClient
{
    Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> ApproveOperationTaskAsync(string operationTaskId, DecideOperationApprovalRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> RejectOperationTaskAsync(string operationTaskId, DecideOperationApprovalRequest request, CancellationToken cancellationToken = default);
    Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default);
    Task<PendingOperationTasksResponse> ClaimOperationTasksAsync(ClaimOperationTasksRequest request, CancellationToken cancellationToken = default);
    Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> AbandonOperationTaskLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> HeartbeatOperationTaskLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, CancellationToken cancellationToken = default);
    Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default);
}

public sealed class HttpOpsClient(HttpClient httpClient, ConnectorHostCredential? credential = null) : IOpsClient
{
    public async Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/operation-tasks", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<OperationTaskResponse>(response, cancellationToken);
    }

    public async Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        using var response = await httpClient.GetAsync($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<OperationTaskResponse>(response, cancellationToken);
    }

    public async Task<OperationTaskResponse> ApproveOperationTaskAsync(string operationTaskId, DecideOperationApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        using var response = await httpClient.PostAsJsonAsync($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}/approval/approve", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<OperationTaskResponse>(response, cancellationToken);
    }

    public async Task<OperationTaskResponse> RejectOperationTaskAsync(string operationTaskId, DecideOperationApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        using var response = await httpClient.PostAsJsonAsync($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}/approval/reject", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<OperationTaskResponse>(response, cancellationToken);
    }

    public async Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ops/v1/operation-tasks/pending?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&connectorHostId={Uri.EscapeDataString(connectorHostId)}&take={take}");
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<PendingOperationTasksResponse>(response, cancellationToken);
    }

    public async Task<PendingOperationTasksResponse> ClaimOperationTasksAsync(ClaimOperationTasksRequest claim, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ops/v1/operation-tasks/claims") { Content = JsonContent.Create(claim) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<PendingOperationTasksResponse>(response, cancellationToken);
    }

    public async Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest auditIntent, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/audit-intents", auditIntent, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<AuditIntentResponse>(response, cancellationToken);
    }

    public async Task<OperationTaskResponse> AbandonOperationTaskLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest abandon, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ops/v1/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/lease/abandon") { Content = JsonContent.Create(abandon) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<OperationTaskResponse>(response, cancellationToken);
    }

    public async Task<OperationTaskResponse> HeartbeatOperationTaskLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest heartbeat, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ops/v1/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/lease/heartbeat") { Content = JsonContent.Create(heartbeat) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<OperationTaskResponse>(response, cancellationToken);
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
