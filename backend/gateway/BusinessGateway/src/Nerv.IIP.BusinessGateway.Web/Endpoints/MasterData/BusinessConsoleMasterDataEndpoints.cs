using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("listBusinessConsoleSkus")]
[AllowAnonymous]
public sealed class ListBusinessConsoleSkusEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("createBusinessConsoleSku")]
[AllowAnonymous]
public sealed class CreateBusinessConsoleSkuEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}
