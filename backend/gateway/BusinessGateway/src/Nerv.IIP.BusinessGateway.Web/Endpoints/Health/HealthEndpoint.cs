using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Health;

[HttpGet("/health")]
[BusinessGatewayOperationId("HealthEndpoint")]
[AllowAnonymous]
public sealed class HealthEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await HttpContext.Response.WriteAsync("Healthy", ct);
    }
}
