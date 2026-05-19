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
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "Ops returned an empty operation task response.", cancellationToken)
            ?? throw new HttpRequestException("Ops returned an empty operation task response.");
    }

    public async Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        using var response = await httpClient.GetAsync($"/api/ops/v1/operation-tasks/{escapedOperationTaskId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "Ops returned an empty operation task response.", cancellationToken)
            ?? throw new HttpRequestException("Ops returned an empty operation task response.");
    }

    private static async Task<T?> ReadResponseDataAsync<T>(
        HttpResponseMessage response,
        string emptyMessage,
        CancellationToken cancellationToken)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken: cancellationToken)
            ?? throw new HttpRequestException(emptyMessage);
        if (!envelope.Success)
        {
            throw new HttpRequestException(envelope.Message);
        }

        return envelope.Data;
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
