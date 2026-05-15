using System.Net.Http.Json;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

public interface IGatewayOpsClient
{
    Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken);
    Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken);
}

public sealed class GatewayOpsClient(HttpClient httpClient) : IGatewayOpsClient
{
    public async Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/operation-tasks", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OperationTaskResponse>(cancellationToken: cancellationToken)
            ?? throw new HttpRequestException("Ops returned an empty operation task response.");
    }

    public async Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        return await httpClient.GetFromJsonAsync<OperationTaskResponse>($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}", cancellationToken)
            ?? throw new HttpRequestException("Ops returned an empty operation task response.");
    }
}
