using System.Net.Http.Json;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public sealed class HttpOrderUrgencyArchiveStore(
    HttpClient httpClient,
    IConfiguration configuration) : IOrderUrgencyArchiveStore
{
    public Task<VersionedArchiveEvidence> PutAsync(
        PutVersionedArchiveRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PutVersionedArchiveRequest, VersionedArchiveEvidence>(
            "/api/files/internal/v1/versioned-archives/put", request, cancellationToken);

    public Task<GetVersionedArchiveResponse> GetAsync(
        GetVersionedArchiveRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<GetVersionedArchiveRequest, GetVersionedArchiveResponse>(
            "/api/files/internal/v1/versioned-archives/get", request, cancellationToken);

    public async Task DeleteAsync(
        DeleteVersionedArchiveRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateMessage("/api/files/internal/v1/versioned-archives/delete", request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"FileStorage exact-version archive deletion failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateMessage(path, request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"FileStorage versioned archive request failed with HTTP {(int)response.StatusCode}.");
        }
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidOperationException("FileStorage versioned archive response body was empty.");
    }

    private HttpRequestMessage CreateMessage<T>(string path, T request)
    {
        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException("FileStorage:BaseUrl is required for urgency retention.");
        }
        var token = configuration["InternalService:BearerToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("InternalService:BearerToken is required for urgency retention.");
        }
        var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return message;
    }
}
