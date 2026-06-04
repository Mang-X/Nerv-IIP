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
    private readonly IInternalServiceTokenProvider _tokenProvider;

    public ListBusinessConsoleWmsInboundOrdersEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IBusinessInventoryClient inventory,
        IInternalServiceTokenProvider tokenProvider)
        : base(auth, BusinessGatewayPermissions.WmsReceiptsRead)
    {
        _auth = auth;
        _wms = wms;
        _inventory = inventory;
        _tokenProvider = tokenProvider;
    }

    protected override string OrganizationId(BusinessConsoleWmsInboundOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsInboundOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsInboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsInboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var response = await _wms.ListInboundOrdersAsync(
            _tokenProvider.BearerToken,
            new BusinessConsoleWmsListRequest(request.OrganizationId, request.EnvironmentId),
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
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsInboundOrder")]
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
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsListRequest, BusinessConsoleWmsOutboundOrderListResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override string OrganizationId(BusinessConsoleWmsListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsOutboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListOutboundOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
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
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsOutboundOrder")]
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
[HttpPost("/api/business-console/v1/wms/count-executions/{countExecutionId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsCountExecution")]
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
    }
}
