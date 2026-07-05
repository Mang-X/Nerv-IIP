using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Health;

[HttpGet("/health")]
[BusinessGatewayOperationId("HealthEndpoint")]
[AllowAnonymous]
public sealed class HealthEndpoint(BusinessGatewayDownstreamHealthState healthState) : EndpointWithoutRequest
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
