using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Instances;

[HttpGet("/api/console/v1/instances")]
[AllowAnonymous]
public sealed class ListInstancesEndpoint(IAppHubClient appHub, IAppCache cache) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        var pageNumber = Query<int>("pageNumber", isRequired: false);
        var pageSize = Query<int>("pageSize", isRequired: false);
        var search = Query<string>("search", isRequired: false);
        var query = new InstanceListQuery(organizationId, environmentId, pageNumber == 0 ? 1 : pageNumber, pageSize == 0 ? 20 : pageSize, search);
        var key = NervIipCacheKeys.GatewayInstanceList(organizationId, environmentId, NervIipCacheKeys.HashQuery(query));

        try
        {
            var response = await cache.GetOrCreateAsync(key, () => appHub.QueryInstancesAsync(query, ct), TimeSpan.FromSeconds(5));
            await HttpContext.Response.WriteAsJsonAsync(response, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/instances/{instanceKey}")]
[AllowAnonymous]
public sealed class GetInstanceDetailEndpoint(IAppHubClient appHub, IAppCache cache) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        var instanceKey = Route<string>("instanceKey")!;
        var key = NervIipCacheKeys.GatewayInstanceDetail(organizationId, environmentId, instanceKey);

        try
        {
            var response = await cache.GetOrCreateAsync(key, () => appHub.GetInstanceAsync(organizationId, environmentId, instanceKey, ct), TimeSpan.FromSeconds(5));
            await HttpContext.Response.WriteAsJsonAsync(response, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

internal static class GatewayEndpointResults
{
    public static async Task WriteBadGatewayAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { title = "Bad Gateway", detail = $"AppHub unavailable: {exception.Message}", status = StatusCodes.Status502BadGateway }, cancellationToken);
    }
}
