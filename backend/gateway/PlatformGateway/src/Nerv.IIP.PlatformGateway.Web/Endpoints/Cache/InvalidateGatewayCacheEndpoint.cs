using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Caching;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Cache;

[HttpPost("/internal/gateway/cache/invalidate")]
[AllowAnonymous]
public sealed class InvalidateGatewayCacheEndpoint(IAppCache cache) : EndpointWithoutRequest
{
    public override Task HandleAsync(CancellationToken ct)
    {
        cache.Clear();
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }
}
