using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

public interface IGatewayOpsClient
{
    Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken);
    Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken);
}

public sealed class GatewayOpsClient(HttpClient httpClient, IInternalServiceTokenProvider internalServiceToken) : IGatewayOpsClient
{
    public async Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = CreateInternalServiceRequest(HttpMethod.Post, "/api/ops/v1/operation-tasks");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "Ops returned an empty operation task response.", cancellationToken)
            ?? throw new HttpRequestException("Ops returned an empty operation task response.");
    }

    public async Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        var escapedOperationTaskId = Uri.EscapeDataString(operationTaskId);
        using var request = CreateInternalServiceRequest(HttpMethod.Get, $"/api/ops/v1/operation-tasks/{escapedOperationTaskId}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadResponseDataAsync<OperationTaskResponse>(response, "Ops returned an empty operation task response.", cancellationToken)
            ?? throw new HttpRequestException("Ops returned an empty operation task response.");
    }

    private HttpRequestMessage CreateInternalServiceRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalServiceToken.BearerToken);
        return request;
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
