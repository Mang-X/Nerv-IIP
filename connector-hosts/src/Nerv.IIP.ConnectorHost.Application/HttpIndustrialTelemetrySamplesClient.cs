using System.Net.Http.Json;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed class HttpIndustrialTelemetrySamplesClient(HttpClient httpClient)
    : IIndustrialTelemetrySamplesClient
{
    public async Task RecordSampleAsync(
        RecordIndustrialTelemetrySampleRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/business/v1/iiot/samples",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
