using System.Net.Http.Json;
using System.Text.Json;

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

        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ConnectorTagManifestResponseEnvelope>(cancellationToken);
            var acknowledgement = envelope?.Data;
            if (acknowledgement is null
                || string.IsNullOrWhiteSpace(acknowledgement.Disposition)
                || string.IsNullOrWhiteSpace(acknowledgement.AcceptedManifestRevision)
                || acknowledgement.AcceptedManifestObservedAtUtc == default)
            {
                throw InvalidAcknowledgement();
            }

            return acknowledgement;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw InvalidAcknowledgement();
        }
    }

    private static HttpRequestException InvalidAcknowledgement() =>
        new("Connector tag manifest endpoint returned an invalid acknowledgement.");

    private sealed record ConnectorTagManifestResponseEnvelope(ConnectorTagManifestAcknowledgement? Data);
}
