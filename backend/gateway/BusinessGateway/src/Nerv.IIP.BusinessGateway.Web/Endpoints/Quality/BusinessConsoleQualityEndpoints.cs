using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Quality;

public sealed class SubmitBusinessConsoleQualityNcrDispositionRequest
{
    public string NcrId { get; set; } = string.Empty;
}

public sealed class CloseBusinessConsoleQualityNcrRequest
{
    public string NcrId { get; set; } = string.Empty;
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-plans")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionPlans")]
[AllowAnonymous]
public sealed class ListBusinessConsoleQualityInspectionPlansEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-records")]
[BusinessGatewayOperationId("createBusinessConsoleQualityInspectionRecord")]
[AllowAnonymous]
public sealed class CreateBusinessConsoleQualityInspectionRecordEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/ncrs")]
[BusinessGatewayOperationId("listBusinessConsoleQualityNcrs")]
[AllowAnonymous]
public sealed class ListBusinessConsoleQualityNcrsEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/disposition")]
[BusinessGatewayOperationId("submitBusinessConsoleQualityNcrDisposition")]
[AllowAnonymous]
public sealed class SubmitBusinessConsoleQualityNcrDispositionEndpoint
    : Endpoint<SubmitBusinessConsoleQualityNcrDispositionRequest>
{
    public override async Task HandleAsync(SubmitBusinessConsoleQualityNcrDispositionRequest req, CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/close")]
[BusinessGatewayOperationId("closeBusinessConsoleQualityNcr")]
[AllowAnonymous]
public sealed class CloseBusinessConsoleQualityNcrEndpoint : Endpoint<CloseBusinessConsoleQualityNcrRequest>
{
    public override async Task HandleAsync(CloseBusinessConsoleQualityNcrRequest req, CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}
