using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using Nerv.IIP.PlatformGateway.Web.Application.Resilience;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Health;

[HttpGet("/health")]
[GatewayOperationId("HealthEndpoint")]
[AllowAnonymous]
public sealed class HealthEndpoint(GatewayDownstreamHealthState healthState) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        var degraded = healthState.Snapshot()
            .Where(entry => entry.Status == "degraded")
            .Select(entry => entry.Downstream)
            .ToArray();
        var message = degraded.Length == 0
            ? "Healthy"
            : $"Degraded: {string.Join(", ", degraded)}";
        await HttpContext.Response.WriteAsync(message, ct);
    }
}

[HttpGet("/internal/gateway/v1/build-info")]
[GatewayOperationId("GetBuildInfoEndpoint")]
[AllowAnonymous]
public sealed class GetBuildInfoEndpoint : EndpointWithoutRequest<ResponseData<object>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        object response = new { service = "PlatformGateway", slice = "first-vertical-slice" };
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
