using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Inventory;

public sealed class ConfirmBusinessConsoleInventoryCountAdjustmentRequest
{
    public string CountTaskId { get; set; } = string.Empty;
}

[Tags("Business Console Inventory")]
[HttpGet("/api/business-console/v1/inventory/availability")]
[BusinessGatewayOperationId("getBusinessConsoleInventoryAvailability")]
[AllowAnonymous]
public sealed class GetBusinessConsoleInventoryAvailabilityEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/movements")]
[BusinessGatewayOperationId("postBusinessConsoleInventoryMovement")]
[AllowAnonymous]
public sealed class PostBusinessConsoleInventoryMovementEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/count-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleInventoryCountTask")]
[AllowAnonymous]
public sealed class CreateBusinessConsoleInventoryCountTaskEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments")]
[BusinessGatewayOperationId("confirmBusinessConsoleInventoryCountAdjustment")]
[AllowAnonymous]
public sealed class ConfirmBusinessConsoleInventoryCountAdjustmentEndpoint
    : Endpoint<ConfirmBusinessConsoleInventoryCountAdjustmentRequest>
{
    public override async Task HandleAsync(ConfirmBusinessConsoleInventoryCountAdjustmentRequest req, CancellationToken ct)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status501NotImplemented, "not-implemented", ct);
    }
}
