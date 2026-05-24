using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Inventory;

public sealed class ConfirmBusinessConsoleInventoryCountAdjustmentRequest
{
    public string CountTaskId { get; set; } = string.Empty;
}

[Tags("Business Console Inventory")]
[HttpGet("/api/business-console/v1/inventory/availability")]
[BusinessGatewayOperationId("getBusinessConsoleInventoryAvailability")]
public sealed class GetBusinessConsoleInventoryAvailabilityEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.InventoryLedgerRead);

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/movements")]
[BusinessGatewayOperationId("postBusinessConsoleInventoryMovement")]
public sealed class PostBusinessConsoleInventoryMovementEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.InventoryMovementsCreate);

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/count-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleInventoryCountTask")]
public sealed class CreateBusinessConsoleInventoryCountTaskEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.InventoryCountsManage);

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments")]
[BusinessGatewayOperationId("confirmBusinessConsoleInventoryCountAdjustment")]
public sealed class ConfirmBusinessConsoleInventoryCountAdjustmentEndpoint
    (IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint<ConfirmBusinessConsoleInventoryCountAdjustmentRequest>(
        auth,
        BusinessGatewayPermissions.InventoryCountsManage);
