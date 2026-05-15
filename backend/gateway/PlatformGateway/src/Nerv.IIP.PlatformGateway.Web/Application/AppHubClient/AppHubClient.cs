using System.Net.Http.Json;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.PlatformGateway.Web;

public interface IAppHubClient
{
    Task<InstanceListResponse> QueryInstancesAsync(InstanceListQuery query, CancellationToken cancellationToken);
    Task<InstanceDetailResponse> GetInstanceAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken);
}

public sealed class HttpAppHubClient(HttpClient httpClient) : IAppHubClient
{
    public async Task<InstanceListResponse> QueryInstancesAsync(InstanceListQuery query, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/internal/apphub/v1/instances/query", query, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InstanceListResponse>(cancellationToken: cancellationToken)
            ?? throw new HttpRequestException("AppHub returned an empty instance list response.");
    }

    public async Task<InstanceDetailResponse> GetInstanceAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/internal/apphub/v1/instances/{Uri.EscapeDataString(instanceKey)}?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InstanceDetailResponse>(cancellationToken: cancellationToken)
            ?? throw new HttpRequestException("AppHub returned an empty instance detail response.");
    }
}
