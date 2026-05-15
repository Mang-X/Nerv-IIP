using FastEndpoints;
using Microsoft.AspNetCore.Authorization;

namespace Nerv.IIP.Iam.Web.Endpoints.Health;

[HttpGet("/health")]
[AllowAnonymous]
public sealed class HealthEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsync("Healthy", ct);
    }
}

[HttpGet("/internal/iam/v1/build-info")]
[AllowAnonymous]
public sealed class GetBuildInfoEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(new { service = "Iam", seeded = true }, ct);
    }
}
