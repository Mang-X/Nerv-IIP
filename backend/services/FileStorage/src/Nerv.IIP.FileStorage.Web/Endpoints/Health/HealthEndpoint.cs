using FastEndpoints;
using Microsoft.AspNetCore.Authorization;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Health;

[HttpGet("/health")]
[AllowAnonymous]
public sealed class HealthEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsync("Healthy", ct);
    }
}

[HttpGet("/internal/file-storage/v1/build-info")]
[AllowAnonymous]
public sealed class GetBuildInfoEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(new { service = "FileStorage", slice = "first-iteration-skeleton" }, ct);
    }
}
