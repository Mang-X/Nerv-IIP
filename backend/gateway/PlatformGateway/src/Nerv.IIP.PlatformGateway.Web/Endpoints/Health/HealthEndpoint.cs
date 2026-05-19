using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Health;

[HttpGet("/health")]
[AllowAnonymous]
public sealed class HealthEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await HttpContext.Response.WriteAsync("Healthy", ct);
    }
}

[HttpGet("/internal/gateway/v1/build-info")]
[AllowAnonymous]
public sealed class GetBuildInfoEndpoint : EndpointWithoutRequest<ResponseData<object>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        object response = new { service = "PlatformGateway", slice = "first-vertical-slice" };
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
