using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Inventory;

[Tags("Business Console Inventory")]
[HttpGet("/api/business-console/v1/inventory/availability")]
[BusinessGatewayOperationId("getBusinessConsoleInventoryAvailability")]
public sealed class GetBusinessConsoleInventoryAvailabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleInventoryAvailabilityRequest, BusinessConsoleInventoryAvailabilityResponse>(
        auth,
        BusinessGatewayPermissions.InventoryLedgerRead)
{
    protected override string OrganizationId(BusinessConsoleInventoryAvailabilityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleInventoryAvailabilityRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleInventoryAvailabilityResponse> ForwardAsync(
        BusinessConsoleInventoryAvailabilityRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        inventory.GetAvailabilityAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/movements")]
[BusinessGatewayOperationId("postBusinessConsoleInventoryMovement")]
public sealed class PostBusinessConsoleInventoryMovementEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePostStockMovementRequest, BusinessConsolePostStockMovementResponse>(
        auth,
        BusinessGatewayPermissions.InventoryMovementsCreate)
{
    protected override string OrganizationId(BusinessConsolePostStockMovementRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePostStockMovementRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsolePostStockMovementResponse> ForwardAsync(
        BusinessConsolePostStockMovementRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        inventory.PostMovementAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/count-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleInventoryCountTask")]
public sealed class CreateBusinessConsoleInventoryCountTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateStockCountTaskRequest, BusinessConsoleCreateStockCountTaskResponse>(
        auth,
        BusinessGatewayPermissions.InventoryCountsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateStockCountTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateStockCountTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateStockCountTaskResponse> ForwardAsync(
        BusinessConsoleCreateStockCountTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        inventory.CreateCountTaskAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Inventory")]
[HttpPost("/api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments")]
[BusinessGatewayOperationId("confirmBusinessConsoleInventoryCountAdjustment")]
public sealed class ConfirmBusinessConsoleInventoryCountAdjustmentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleConfirmStockCountAdjustmentRequest, BusinessConsoleConfirmStockCountAdjustmentResponse>(
        auth,
        BusinessGatewayPermissions.InventoryCountsManage)
{
    protected override string OrganizationId(BusinessConsoleConfirmStockCountAdjustmentRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleConfirmStockCountAdjustmentRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ForwardAsync(
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var countTaskId = Route<string>("countTaskId") ?? request.CountTaskId;
        var downstreamRequest = request with { CountTaskId = countTaskId };
        return inventory.ConfirmCountAdjustmentAsync(
            tokenProvider.BearerToken,
            countTaskId,
            downstreamRequest,
            cancellationToken);
    }
}
