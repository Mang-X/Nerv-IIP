using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Caching;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Cache;

[HttpPost("/internal/gateway/cache/invalidate")]
[GatewayOperationId("InvalidateGatewayCacheEndpoint")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class InvalidateGatewayCacheEndpoint(IAppCache cache) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            cache.RemoveByTag(NervIipCacheTags.Gateway);
        }
        catch (Exception)
        {
            // The adapter logs safe operation metadata. Do not let provider exceptions reach
            // the HTTP server's exception logger, which includes raw Redis keys and errors.
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Gateway cache invalidation unavailable.", ct);
            return;
        }

        // Submission succeeded; subscribers process the backplane notification asynchronously.
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
