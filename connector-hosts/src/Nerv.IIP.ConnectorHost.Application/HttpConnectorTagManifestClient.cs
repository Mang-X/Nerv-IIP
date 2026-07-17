using System.Net.Http.Json;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed class HttpConnectorTagManifestClient(HttpClient httpClient) : IConnectorTagManifestClient
{
    public async Task<ConnectorTagManifestAcknowledgement> ReportAsync(
        ConnectorTagManifestReport report,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/business/v1/iiot/connector-tag-manifests",
            report,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Connector tag manifest endpoint returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ConnectorTagManifestAcknowledgement>(cancellationToken)
            ?? throw new HttpRequestException("Connector tag manifest endpoint returned an invalid acknowledgement.");
    }
}
