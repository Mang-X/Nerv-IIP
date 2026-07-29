using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Wms;

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders")]
[BusinessGatewayOperationId("createBusinessConsoleWmsInboundOrder")]
public sealed class CreateBusinessConsoleWmsInboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsInboundOrderRequest, BusinessConsoleCreateWmsInboundOrderResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsInboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsInboundOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsInboundOrderResponse> ForwardAsync(
        BusinessConsoleCreateWmsInboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.CreateInboundOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/inbound-orders")]
[BusinessGatewayOperationId("listBusinessConsoleWmsInboundOrders")]
public sealed class ListBusinessConsoleWmsInboundOrdersEndpoint
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsInboundOrderListRequest, BusinessConsoleWmsInboundOrderListResponse>
{
    private readonly IBusinessGatewayAuthorizationClient _auth;
    private readonly IBusinessWmsClient _wms;
    private readonly IBusinessInventoryClient _inventory;
    private readonly PrincipalWorkScopeResolver _workScopeResolver;
    private readonly IInternalServiceTokenProvider _tokenProvider;

    public ListBusinessConsoleWmsInboundOrdersEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IBusinessInventoryClient inventory,
        PrincipalWorkScopeResolver workScopeResolver,
        IInternalServiceTokenProvider tokenProvider)
        : base(auth, BusinessGatewayPermissions.WmsReceiptsRead)
    {
        _auth = auth;
        _wms = wms;
        _inventory = inventory;
        _workScopeResolver = workScopeResolver;
        _tokenProvider = tokenProvider;
    }

    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsInboundOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsInboundOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsInboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsInboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            _workScopeResolver,
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        var response = await _wms.ListInboundOrdersAsync(
            _tokenProvider.BearerToken,
            new BusinessWmsScopedListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword,
                scope.AssignedOperatorUserIds,
                scope.AssignedTeamIds,
                scope.SiteCodes,
                scope.OrganizationWideScope),
            request.InboundOrderId,
            cancellationToken);
        var inventoryContext = await TryGetInventoryContextAsync(request, bearerToken, cancellationToken);
        return response with
        {
            InventoryContext = inventoryContext,
            SourceStatus = inventoryContext?.Status ?? "scope-required",
        };
    }

    private async Task<BusinessConsoleWmsInventoryContext?> TryGetInventoryContextAsync(
        BusinessConsoleWmsInboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SkuCode)
            || string.IsNullOrWhiteSpace(request.UomCode)
            || string.IsNullOrWhiteSpace(request.SiteCode))
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "scope-required",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "sku-uom-site-required",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }

        var authorization = await _auth.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.InventoryLedgerRead,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                null),
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "forbidden",
                BusinessGatewayPermissions.InventoryLedgerRead,
                authorization.DenialReason ?? "forbidden",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }

        try
        {
            var availability = await _inventory.GetAvailabilityAsync(
                _tokenProvider.BearerToken,
                new BusinessConsoleInventoryAvailabilityRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId),
                cancellationToken);
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "available",
                BusinessGatewayPermissions.InventoryLedgerRead,
                null,
                availability.SkuCode,
                availability.UomCode,
                availability.SiteCode,
                availability.LocationCode,
                availability.LotNo,
                availability.SerialNo,
                availability.QualityStatus,
                availability.OwnerType,
                availability.OwnerId,
                availability.OnHandQuantity,
                availability.ReservedQuantity,
                availability.AvailableQuantity,
                availability.Items);
        }
        catch (BusinessServiceProxyException)
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "unavailable",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "downstream-request-failed",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }
        catch (HttpRequestException)
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "unavailable",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "downstream-unavailable",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleWmsPutawayTask")]
public sealed class CreateBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsPutawayTaskRequest, BusinessConsoleCreateWmsWarehouseTaskResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsPutawayTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsPutawayTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsWarehouseTaskResponse> ForwardAsync(
        BusinessConsoleCreateWmsPutawayTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inboundOrderId = Route<string>("inboundOrderId") ?? request.InboundOrderId;
        return wms.CreatePutawayTaskAsync(
            tokenProvider.BearerToken,
            inboundOrderId,
            request with { InboundOrderId = inboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/putaway-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleWmsPutawayTasks")]
public sealed class ListBusinessConsoleWmsPutawayTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsWarehouseTaskListRequest, BusinessConsoleWmsWarehouseTaskListResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskListResponse> ForwardAsync(
        BusinessConsoleWmsWarehouseTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver,
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return await wms.ListPutawayTasksAsync(
            tokenProvider.BearerToken,
            BusinessConsoleWmsScopeProjection.ToTaskListRequest(request, scope),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleWmsPutawayTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class StartBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleStartWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleStartWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.StartPutawayTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToStartRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/progress")]
[BusinessGatewayOperationId("recordBusinessConsoleWmsPutawayTaskProgress")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class RecordBusinessConsoleWmsPutawayTaskProgressEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordWmsWarehouseTaskProgressRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleRecordWmsWarehouseTaskProgressRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.RecordPutawayTaskProgressAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToProgressRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/exception")]
[BusinessGatewayOperationId("reportBusinessConsoleWmsPutawayTaskException")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ReportBusinessConsoleWmsPutawayTaskExceptionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReportWmsWarehouseTaskExceptionRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleReportWmsWarehouseTaskExceptionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.ReportPutawayTaskExceptionAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToExceptionRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsPutawayTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleCompleteWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.CompletePutawayTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToCompleteRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsInboundOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleWmsInboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsInboundOrderRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteWmsInboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsInboundOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleCompleteWmsInboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inboundOrderId = Route<string>("inboundOrderId") ?? request.InboundOrderId;
        return wms.CompleteInboundOrderAsync(
            tokenProvider.BearerToken,
            inboundOrderId,
            request with { InboundOrderId = inboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders")]
[BusinessGatewayOperationId("createBusinessConsoleWmsOutboundOrder")]
public sealed class CreateBusinessConsoleWmsOutboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsOutboundOrderRequest, BusinessConsoleCreateWmsOutboundOrderResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsOutboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsOutboundOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsOutboundOrderResponse> ForwardAsync(
        BusinessConsoleCreateWmsOutboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.CreateOutboundOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/outbound-orders")]
[BusinessGatewayOperationId("listBusinessConsoleWmsOutboundOrders")]
public sealed class ListBusinessConsoleWmsOutboundOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsOutboundOrderListRequest, BusinessConsoleWmsOutboundOrderListResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsOutboundOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsOutboundOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsOutboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsOutboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver,
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.WmsShipmentsRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return await wms.ListOutboundOrdersAsync(
            tokenProvider.BearerToken,
            new BusinessWmsScopedListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword,
                scope.AssignedOperatorUserIds,
                scope.AssignedTeamIds,
                scope.SiteCodes,
                scope.OrganizationWideScope),
            request.OutboundOrderId,
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleWmsPickingTask")]
public sealed class CreateBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsPickingTaskRequest, BusinessConsoleCreateWmsWarehouseTaskResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsPickingTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsPickingTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsWarehouseTaskResponse> ForwardAsync(
        BusinessConsoleCreateWmsPickingTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var outboundOrderId = Route<string>("outboundOrderId") ?? request.OutboundOrderId;
        return wms.CreatePickingTaskAsync(
            tokenProvider.BearerToken,
            outboundOrderId,
            request with { OutboundOrderId = outboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/picking-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleWmsPickingTasks")]
public sealed class ListBusinessConsoleWmsPickingTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsWarehouseTaskListRequest, BusinessConsoleWmsWarehouseTaskListResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskListResponse> ForwardAsync(
        BusinessConsoleWmsWarehouseTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver,
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.WmsShipmentsRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return await wms.ListPickingTasksAsync(
            tokenProvider.BearerToken,
            BusinessConsoleWmsScopeProjection.ToTaskListRequest(request, scope),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleWmsPickingTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class StartBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleStartWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleStartWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsShipmentsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.StartPickingTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToStartRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/progress")]
[BusinessGatewayOperationId("recordBusinessConsoleWmsPickingTaskProgress")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class RecordBusinessConsoleWmsPickingTaskProgressEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordWmsWarehouseTaskProgressRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleRecordWmsWarehouseTaskProgressRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsShipmentsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.RecordPickingTaskProgressAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToProgressRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/exception")]
[BusinessGatewayOperationId("reportBusinessConsoleWmsPickingTaskException")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ReportBusinessConsoleWmsPickingTaskExceptionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReportWmsWarehouseTaskExceptionRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleReportWmsWarehouseTaskExceptionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsShipmentsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.ReportPickingTaskExceptionAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToExceptionRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsPickingTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleCompleteWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver, AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.WmsShipmentsManage, request.ScopeKind, request.ScopeId, cancellationToken);
        return await wms.CompletePickingTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            scope.ToCompleteRequest(request with { WarehouseTaskId = taskId }),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsOutboundOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleWmsOutboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsOutboundOrderRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteWmsOutboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsOutboundOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleCompleteWmsOutboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var outboundOrderId = Route<string>("outboundOrderId") ?? request.OutboundOrderId;
        return wms.CompleteOutboundOrderAsync(
            tokenProvider.BearerToken,
            outboundOrderId,
            request with { OutboundOrderId = outboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry")]
[BusinessGatewayOperationId("retryBusinessConsoleWmsOutboundInventoryPosting")]
public sealed class RetryBusinessConsoleWmsOutboundInventoryPostingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRetryWmsOutboundInventoryPostingRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleRetryWmsOutboundInventoryPostingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRetryWmsOutboundInventoryPostingRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleRetryWmsOutboundInventoryPostingRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var outboundOrderId = Route<string>("outboundOrderId") ?? request.OutboundOrderId;
        return wms.RetryOutboundInventoryPostingAsync(
            tokenProvider.BearerToken,
            outboundOrderId,
            request with { OutboundOrderId = outboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/count-executions")]
[BusinessGatewayOperationId("createBusinessConsoleWmsCountExecution")]
public sealed class CreateBusinessConsoleWmsCountExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsCountExecutionRequest, BusinessConsoleCreateWmsCountExecutionResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsCountExecutionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsCountExecutionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsCountExecutionResponse> ForwardAsync(
        BusinessConsoleCreateWmsCountExecutionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.CreateCountExecutionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/count-executions")]
[BusinessGatewayOperationId("listBusinessConsoleWmsCountExecutions")]
public sealed class ListBusinessConsoleWmsCountExecutionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsCountExecutionListRequest, BusinessConsoleWmsCountExecutionListResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsCountExecutionListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsCountExecutionListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsCountExecutionListResponse> ForwardAsync(
        BusinessConsoleWmsCountExecutionListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scope = await BusinessConsoleWmsScopeProjection.ResolveAsync(
            workScopeResolver,
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.WmsReceiptsRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return await wms.ListCountExecutionsAsync(
            tokenProvider.BearerToken,
            new BusinessWmsCountExecutionListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.LocationCode,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword,
                request.CountExecutionId,
                scope.AssignedOperatorUserIds,
                scope.AssignedTeamIds,
                scope.SiteCodes,
                scope.OrganizationWideScope),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/count-executions/{countExecutionId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsCountExecution")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleWmsCountExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsCountExecutionRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteWmsCountExecutionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsCountExecutionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleCompleteWmsCountExecutionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var countExecutionId = Route<string>("countExecutionId") ?? request.CountExecutionId;
        return wms.CompleteCountExecutionAsync(
            tokenProvider.BearerToken,
            countExecutionId,
            request with { CountExecutionId = countExecutionId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch")]
[BusinessGatewayOperationId("dispatchBusinessConsoleWmsWcsTask")]
public sealed class DispatchBusinessConsoleWmsWcsTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDispatchWmsWcsTaskRequest, BusinessConsoleDispatchWmsWcsTaskResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleDispatchWmsWcsTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDispatchWmsWcsTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleDispatchWmsWcsTaskResponse> ForwardAsync(
        BusinessConsoleDispatchWmsWcsTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var warehouseTaskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        return wms.DispatchWcsTaskAsync(
            tokenProvider.BearerToken,
            warehouseTaskId,
            request with { WarehouseTaskId = warehouseTaskId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/fail")]
[BusinessGatewayOperationId("failBusinessConsoleWmsWcsTask")]
public sealed class FailBusinessConsoleWmsWcsTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleFailWmsWcsTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleFailWmsWcsTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleFailWmsWcsTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleFailWmsWcsTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var externalTaskId = Route<string>("externalTaskId") ?? request.ExternalTaskId;
        return wms.FailWcsTaskAsync(
            tokenProvider.BearerToken,
            externalTaskId,
            request with { ExternalTaskId = externalTaskId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsWcsTask")]
public sealed class CompleteBusinessConsoleWmsWcsTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsWcsTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteWmsWcsTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsWcsTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleCompleteWmsWcsTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var externalTaskId = Route<string>("externalTaskId") ?? request.ExternalTaskId;
        return wms.CompleteWcsTaskAsync(
            tokenProvider.BearerToken,
            externalTaskId,
            request with { ExternalTaskId = externalTaskId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/wcs-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleWmsWcsTasks")]
public sealed class ListBusinessConsoleWmsWcsTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsWcsTaskListRequest, BusinessConsoleWmsWcsTaskListResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleWmsWcsTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWcsTaskListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsWcsTaskListResponse> ForwardAsync(
        BusinessConsoleWmsWcsTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListWcsTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/receiving-quality-gates")]
[BusinessGatewayOperationId("listBusinessConsoleWmsReceivingQualityGates")]
public sealed class ListBusinessConsoleWmsReceivingQualityGatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsReceivingQualityGateListRequest, BusinessConsoleWmsReceivingQualityGateListResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleWmsReceivingQualityGateListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsReceivingQualityGateListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsReceivingQualityGateListResponse> ForwardAsync(
        BusinessConsoleWmsReceivingQualityGateListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListReceivingQualityGatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/supplier-return-requests")]
[BusinessGatewayOperationId("listBusinessConsoleWmsSupplierReturnRequests")]
public sealed class ListBusinessConsoleWmsSupplierReturnRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsListRequest, BusinessConsoleWmsSupplierReturnListResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleWmsListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsSupplierReturnListResponse> ForwardAsync(
        BusinessConsoleWmsListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListSupplierReturnRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

internal sealed record BusinessConsoleWmsScopeProjection(
    string? AssignedOperatorUserIds,
    string? AssignedTeamIds,
    string? SiteCodes,
    bool OrganizationWideScope,
    string ActorUserId)
{
    public static async Task<BusinessConsoleWmsScopeProjection> ResolveAsync(
        PrincipalWorkScopeResolver resolver,
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? scopeKind,
        string? scopeId,
        CancellationToken cancellationToken)
    {
        var scope = await resolver.ResolveAsync(
            authorization,
            organizationId,
            environmentId,
            permissionCode,
            scopeKind,
            scopeId,
            cancellationToken);
        if (scope.Kind is "work-center" or "workshop")
        {
            throw new BusinessServiceProxyException(
                System.Net.HttpStatusCode.Forbidden,
                "wms-work-scope-not-supported");
        }

        var actorUserId = authorization?.PrincipalId;
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new BusinessServiceProxyException(
                System.Net.HttpStatusCode.Forbidden,
                "wms-principal-unresolved");
        }

        return new BusinessConsoleWmsScopeProjection(
            Join(scope.AssignedUserIds),
            Join(scope.TeamIds),
            Join(scope.SiteCodes),
            string.Equals(scope.Kind, "organization", StringComparison.Ordinal),
            actorUserId);
    }

    public static BusinessWmsWarehouseTaskListRequest ToTaskListRequest(
        BusinessConsoleWmsWarehouseTaskListRequest request,
        BusinessConsoleWmsScopeProjection scope) =>
        new(
            request.OrganizationId,
            request.EnvironmentId,
            request.LocationCode,
            request.LotNo,
            request.Skip,
            request.Take,
            request.Status,
            request.Keyword,
            scope.AssignedOperatorUserIds,
            scope.AssignedTeamIds,
            scope.SiteCodes,
            scope.OrganizationWideScope);

    public IReadOnlyCollection<string>? AuthorizedTeamIds => Split(AssignedTeamIds);

    public IReadOnlyCollection<string>? AuthorizedSiteCodes => Split(SiteCodes);

    public BusinessWmsStartWarehouseTaskActionRequest ToStartRequest(
        BusinessConsoleStartWmsWarehouseTaskRequest request) =>
        new(
            request.WarehouseTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            ActorUserId,
            request.IdempotencyKey,
            request.ExpectedVersion,
            AuthorizedTeamIds,
            AuthorizedSiteCodes,
            OrganizationWideScope);

    public BusinessWmsRecordWarehouseTaskProgressActionRequest ToProgressRequest(
        BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) =>
        new(
            request.WarehouseTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            ActorUserId,
            request.IdempotencyKey,
            request.ExpectedVersion,
            request.ExecutedQuantity,
            AuthorizedTeamIds,
            AuthorizedSiteCodes,
            OrganizationWideScope);

    public BusinessWmsReportWarehouseTaskExceptionActionRequest ToExceptionRequest(
        BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) =>
        new(
            request.WarehouseTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            ActorUserId,
            request.IdempotencyKey,
            request.ExpectedVersion,
            request.ExceptionCode,
            request.Reason,
            AuthorizedTeamIds,
            AuthorizedSiteCodes,
            OrganizationWideScope);

    public BusinessWmsCompleteWarehouseTaskActionRequest ToCompleteRequest(
        BusinessConsoleCompleteWmsWarehouseTaskRequest request) =>
        new(
            request.WarehouseTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            ActorUserId,
            request.IdempotencyKey,
            request.ExpectedVersion,
            request.ExecutedQuantity,
            request.DifferenceReason,
            AuthorizedTeamIds,
            AuthorizedSiteCodes,
            OrganizationWideScope);

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(',', values.Order(StringComparer.Ordinal));

    private static IReadOnlyCollection<string>? Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class BusinessConsoleWmsInboundOrderListRequestValidator
    : Validator<BusinessConsoleWmsInboundOrderListRequest>
{
    public BusinessConsoleWmsInboundOrderListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.UomCode).MaximumLength(50);
        RuleFor(x => x.SiteCode).MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.LotNo).MaximumLength(100);
        RuleFor(x => x.SerialNo).MaximumLength(100);
        RuleFor(x => x.QualityStatus).MaximumLength(50);
        RuleFor(x => x.OwnerType).MaximumLength(50);
        RuleFor(x => x.OwnerId).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCreateWmsInboundOrderRequestValidator
    : Validator<BusinessConsoleCreateWmsInboundOrderRequest>
{
    public BusinessConsoleCreateWmsInboundOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InboundOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class BusinessConsoleCreateWmsPutawayTaskRequestValidator
    : Validator<BusinessConsoleCreateWmsPutawayTaskRequest>
{
    public BusinessConsoleCreateWmsPutawayTaskRequestValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TaskNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FromLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ToLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class BusinessConsoleCompleteWmsInboundOrderRequestValidator
    : Validator<BusinessConsoleCompleteWmsInboundOrderRequest>
{
    public BusinessConsoleCompleteWmsInboundOrderRequestValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleForEach(x => x.Lines).SetValidator(new BusinessConsoleWmsInboundLineCaptureInputValidator());
    }
}

public sealed class BusinessConsoleWmsInboundLineCaptureInputValidator
    : Validator<BusinessConsoleWmsInboundLineCaptureInput>
{
    public BusinessConsoleWmsInboundLineCaptureInputValidator()
    {
        RuleFor(x => x.LineNo).NotEmpty().MaximumLength(50);
    }
}

public sealed class BusinessConsoleWmsWarehouseTaskListRequestValidator : Validator<BusinessConsoleWmsWarehouseTaskListRequest>
{
    public BusinessConsoleWmsWarehouseTaskListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.LotNo).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCreateWmsOutboundOrderRequestValidator
    : Validator<BusinessConsoleCreateWmsOutboundOrderRequest>
{
    public BusinessConsoleCreateWmsOutboundOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OutboundOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class BusinessConsoleCreateWmsPickingTaskRequestValidator
    : Validator<BusinessConsoleCreateWmsPickingTaskRequest>
{
    public BusinessConsoleCreateWmsPickingTaskRequestValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TaskNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FromLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ToLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class BusinessConsoleCompleteWmsOutboundOrderRequestValidator
    : Validator<BusinessConsoleCompleteWmsOutboundOrderRequest>
{
    public BusinessConsoleCompleteWmsOutboundOrderRequestValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PackReviewNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleCreateWmsCountExecutionRequestValidator
    : Validator<BusinessConsoleCreateWmsCountExecutionRequest>
{
    public BusinessConsoleCreateWmsCountExecutionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleWmsCountExecutionListRequestValidator : Validator<BusinessConsoleWmsCountExecutionListRequest>
{
    public BusinessConsoleWmsCountExecutionListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCompleteWmsCountExecutionRequestValidator
    : Validator<BusinessConsoleCompleteWmsCountExecutionRequest>
{
    public BusinessConsoleCompleteWmsCountExecutionRequestValidator()
    {
        RuleFor(x => x.CountExecutionId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleStartWmsWarehouseTaskRequestValidator
    : Validator<BusinessConsoleStartWmsWarehouseTaskRequest>
{
    public BusinessConsoleStartWmsWarehouseTaskRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        AddScopeRules();
    }

    private void AddScopeRules()
    {
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleRecordWmsWarehouseTaskProgressRequestValidator
    : Validator<BusinessConsoleRecordWmsWarehouseTaskProgressRequest>
{
    public BusinessConsoleRecordWmsWarehouseTaskProgressRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExecutedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleReportWmsWarehouseTaskExceptionRequestValidator
    : Validator<BusinessConsoleReportWmsWarehouseTaskExceptionRequest>
{
    public BusinessConsoleReportWmsWarehouseTaskExceptionRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExceptionCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCompleteWmsWarehouseTaskRequestValidator
    : Validator<BusinessConsoleCompleteWmsWarehouseTaskRequest>
{
    public BusinessConsoleCompleteWmsWarehouseTaskRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExecutedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DifferenceReason).MaximumLength(500);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleDispatchWmsWcsTaskRequestValidator
    : Validator<BusinessConsoleDispatchWmsWcsTaskRequest>
{
    public BusinessConsoleDispatchWmsWcsTaskRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdapterType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PayloadJson).NotEmpty();
    }
}

public sealed class BusinessConsoleFailWmsWcsTaskRequestValidator
    : Validator<BusinessConsoleFailWmsWcsTaskRequest>
{
    public BusinessConsoleFailWmsWcsTaskRequestValidator()
    {
        RuleFor(x => x.ExternalTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FailureCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FailureMessage).NotEmpty().MaximumLength(500);
    }
}

public sealed class BusinessConsoleCompleteWmsWcsTaskRequestValidator
    : Validator<BusinessConsoleCompleteWmsWcsTaskRequest>
{
    public BusinessConsoleCompleteWmsWcsTaskRequestValidator()
    {
        RuleFor(x => x.ExternalTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CompletionPayloadJson).NotEmpty();
    }
}

public sealed class BusinessConsoleWmsListRequestValidator : Validator<BusinessConsoleWmsListRequest>
{
    public BusinessConsoleWmsListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
    }
}

public sealed class BusinessConsoleWmsOutboundOrderListRequestValidator
    : Validator<BusinessConsoleWmsOutboundOrderListRequest>
{
    public BusinessConsoleWmsOutboundOrderListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.OutboundOrderId)
            .Must(value => value is null || (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty))
            .WithMessage("OutboundOrderId must be a non-empty GUID.");
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleWmsWcsTaskListRequestValidator : Validator<BusinessConsoleWmsWcsTaskListRequest>
{
    public BusinessConsoleWmsWcsTaskListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalTaskId).MaximumLength(150);
        RuleFor(x => x.WarehouseTaskId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
    }
}

public sealed class BusinessConsoleWmsReceivingQualityGateListRequestValidator : Validator<BusinessConsoleWmsReceivingQualityGateListRequest>
{
    public BusinessConsoleWmsReceivingQualityGateListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.GateStatus).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
    }
}

internal static class BusinessConsoleWmsScopeKinds
{
    public static bool Contains(string? value) =>
        value?.Trim() is "self" or "team" or "work-center" or "workshop" or "site" or "organization";
}
