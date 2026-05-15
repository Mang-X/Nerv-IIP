using System.Net.Http.Json;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNervIipCaching(builder.Configuration, "platform-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "platform-gateway");
builder.Services.AddHttpClient<IAppHubClient, HttpAppHubClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5103");
});

var app = builder.Build();
app.UseNervIipCorrelation();

app.MapGet("/health", () => "Healthy");
app.MapGet("/internal/gateway/v1/build-info", () => new { service = "PlatformGateway", slice = "first-vertical-slice" });
app.MapPost("/internal/gateway/cache/invalidate", (IAppCache cache) =>
{
    cache.Clear();
    return Results.NoContent();
});

app.MapGet("/api/console/v1/instances", async (string organizationId, string environmentId, int pageNumber, int pageSize, string? search, IAppHubClient appHub, IAppCache cache, CancellationToken cancellationToken) =>
{
    var query = new InstanceListQuery(organizationId, environmentId, pageNumber, pageSize, search);
    var key = NervIipCacheKeys.GatewayInstanceList(organizationId, environmentId, NervIipCacheKeys.HashQuery(query));
    try
    {
        var response = await cache.GetOrCreateAsync(key, () => appHub.QueryInstancesAsync(query, cancellationToken), TimeSpan.FromSeconds(5));
        return Results.Ok(response);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem($"AppHub unavailable: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/console/v1/instances/{instanceKey}", async (string organizationId, string environmentId, string instanceKey, IAppHubClient appHub, IAppCache cache, CancellationToken cancellationToken) =>
{
    var key = NervIipCacheKeys.GatewayInstanceDetail(organizationId, environmentId, instanceKey);
    try
    {
        var response = await cache.GetOrCreateAsync(key, () => appHub.GetInstanceAsync(organizationId, environmentId, instanceKey, cancellationToken), TimeSpan.FromSeconds(5));
        return Results.Ok(response);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem($"AppHub unavailable: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public partial class Program;

namespace Nerv.IIP.PlatformGateway.Web
{
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
}
