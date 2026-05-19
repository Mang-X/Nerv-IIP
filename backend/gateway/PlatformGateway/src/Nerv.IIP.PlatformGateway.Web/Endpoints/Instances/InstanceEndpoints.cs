using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Instances;

public sealed class ListInstancesRequest
{
    public string OrganizationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public string? Search { get; set; }
}

[HttpGet("/api/console/v1/instances")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListInstancesEndpoint(
    IAppHubClient appHub,
    IAppCache cache,
    IGatewayAuthorizationClient auth) : Endpoint<ListInstancesRequest, InstanceListResponse>
{
    public override async Task HandleAsync(ListInstancesRequest req, CancellationToken ct)
    {
        var principal = await GatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new GatewayPermissionRequirement(
                GatewayPermissions.AppHubInstancesRead,
                req.OrganizationId,
                req.EnvironmentId,
                "application-instance",
                null),
            ct);
        if (principal is null)
        {
            return;
        }

        var pageNumber = req.PageNumber is > 0 ? req.PageNumber.Value : 1;
        var pageSize = req.PageSize is > 0 ? req.PageSize.Value : 20;
        var query = new InstanceListQuery(req.OrganizationId, req.EnvironmentId, pageNumber, pageSize, req.Search);
        var key = NervIipCacheKeys.GatewayInstanceList(req.OrganizationId, req.EnvironmentId, NervIipCacheKeys.HashQuery(query));

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

public sealed class GetInstanceDetailRequest
{
    public string OrganizationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public string InstanceKey { get; set; } = string.Empty;
}

[HttpGet("/api/console/v1/instances/{instanceKey}")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetInstanceDetailEndpoint(
    IAppHubClient appHub,
    IAppCache cache,
    IGatewayAuthorizationClient auth) : Endpoint<GetInstanceDetailRequest, InstanceDetailResponse>
{
    public override async Task HandleAsync(GetInstanceDetailRequest req, CancellationToken ct)
    {
        var principal = await GatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new GatewayPermissionRequirement(
                GatewayPermissions.AppHubInstancesRead,
                req.OrganizationId,
                req.EnvironmentId,
                "application-instance",
                req.InstanceKey),
            ct);
        if (principal is null)
        {
            return;
        }

        var key = NervIipCacheKeys.GatewayInstanceDetail(req.OrganizationId, req.EnvironmentId, req.InstanceKey);

        try
        {
            var response = await cache.GetOrCreateAsync(key, () => appHub.GetInstanceAsync(req.OrganizationId, req.EnvironmentId, req.InstanceKey, ct), TimeSpan.FromSeconds(5));
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
