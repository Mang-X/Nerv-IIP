using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Caching;
using Nerv.IIP.PlatformGateway.Web.Application.Caching;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Cache;

public sealed class InvalidateGatewayCacheScopeRequest
{
    [Required]
    public string OrganizationId { get; set; } = string.Empty;
    [Required]
    public string EnvironmentId { get; set; } = string.Empty;
}

[HttpPost("/internal/gateway/cache/invalidate-scope")]
[GatewayOperationId("invalidateGatewayCacheScope")]
[Authorize(Policy = GatewayCacheInvalidationAuthorization.PolicyName)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(StatusCodes.Status204NoContent)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status400BadRequest)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status503ServiceUnavailable)]
public sealed class InvalidateGatewayCacheScopeEndpoint(IAppCache cache) : Endpoint<InvalidateGatewayCacheScopeRequest>
{
    public override async Task HandleAsync(InvalidateGatewayCacheScopeRequest req, CancellationToken ct)
    {
        if (!IsCanonical(req.OrganizationId) || !IsCanonical(req.EnvironmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest,
                "Organization and environment must be explicit non-empty canonical identifiers.", ct);
            return;
        }
        if (!string.Equals(req.OrganizationId, User.FindFirstValue(ScopedCallerClaimTypes.OrganizationId), StringComparison.Ordinal)
            || !string.Equals(req.EnvironmentId, User.FindFirstValue(ScopedCallerClaimTypes.EnvironmentId), StringComparison.Ordinal))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status403Forbidden,
                "Caller is not authorized for this organization and environment.", ct);
            return;
        }
        ct.ThrowIfCancellationRequested();
        try
        {
            cache.RemoveByTag(GatewayCacheScope.Tag(req.OrganizationId, req.EnvironmentId));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Provider details are logged safely by the existing adapter, as for the legacy operation.
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Gateway cache invalidation unavailable.", ct);
            return;
        }
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}
