using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessAppHubClient
{
    Task<BusinessConsoleConnectorCollectionHealthResponse> GetCollectionHealthAsync(string internalBearerToken, BusinessConsoleConnectorCollectionHealthRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleConnectorCollectionHealthListResponse> GetCollectionHealthListAsync(string internalBearerToken, BusinessConsoleConnectorCollectionHealthListRequest request, CancellationToken cancellationToken);
}

public sealed class HttpBusinessAppHubClient(HttpClient httpClient) : IBusinessAppHubClient
{
    public async Task<BusinessConsoleConnectorCollectionHealthResponse> GetCollectionHealthAsync(string internalBearerToken, BusinessConsoleConnectorCollectionHealthRequest request, CancellationToken cancellationToken)
    {
        var path = $"/internal/apphub/v1/connectors/{Uri.EscapeDataString(request.ConnectorId)}/collection-health?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope<BusinessConsoleConnectorCollectionHealthResponse>>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || envelope?.Data is null) throw new BusinessServiceProxyException(response.StatusCode, envelope?.Message ?? "apphub-request-failed");
        return envelope.Data;
    }

    public async Task<BusinessConsoleConnectorCollectionHealthListResponse> GetCollectionHealthListAsync(string internalBearerToken, BusinessConsoleConnectorCollectionHealthListRequest request, CancellationToken cancellationToken)
    {
        var path = $"/internal/apphub/v1/connectors/collection-health?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope<BusinessConsoleConnectorCollectionHealthListResponse>>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || envelope?.Data is null) throw new BusinessServiceProxyException(response.StatusCode, envelope?.Message ?? "apphub-request-failed");
        return envelope.Data;
    }

    private sealed record ResponseEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
