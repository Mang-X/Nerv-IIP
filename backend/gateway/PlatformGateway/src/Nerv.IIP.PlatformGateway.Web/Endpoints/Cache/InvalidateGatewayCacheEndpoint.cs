using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Caching;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Cache;

[HttpPost("/internal/gateway/cache/invalidate")]
[GatewayOperationId("InvalidateGatewayCacheEndpoint")]
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
