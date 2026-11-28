using System.Net.Http.Json;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.ConnectorHost.Host;

public sealed class HttpConnectorDesiredConfigClient(HttpClient client, ConnectorHostRuntimeContext context)
{
    public async Task<TelemetryConnectorConfigItem[]> GetAsync(CancellationToken cancellationToken)
    {
        var organization = Uri.EscapeDataString(context.OrganizationId);
        var environment = Uri.EscapeDataString(context.EnvironmentId);
        var response = await client.GetFromJsonAsync<DesiredConfigResponse>(
            $"/api/business/v1/iiot/connector-configs/desired?organizationId={organization}&environmentId={environment}",
            cancellationToken);
        return response!.Data;
    }

    private sealed record DesiredConfigResponse(TelemetryConnectorConfigItem[] Data);
}
