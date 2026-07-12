using System.Net.Http.Json;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

public sealed class HttpIndustrialTelemetrySamplesClient(HttpClient httpClient) : IIndustrialTelemetrySamplesClient
{
    public async Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/business/v1/iiot/samples", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
