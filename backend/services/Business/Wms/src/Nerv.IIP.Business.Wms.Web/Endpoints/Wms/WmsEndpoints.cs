using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Endpoints.Wms;

public abstract class WmsEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureWmsContract(WmsEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by WMS endpoints.");
        }

        Tags("Business WMS");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record CreateInboundOrderRequest(string OrganizationId, string EnvironmentId, string InboundOrderNo, string SourceDocumentType, string SourceDocumentId, string SiteCode, IReadOnlyCollection<WmsInboundLineInput> Lines);
public sealed record CreateInboundOrderResponse(InboundOrderId InboundOrderId);
public sealed record ListInboundOrdersRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100, string? Status = null, string? Keyword = null);
public sealed record CreatePutawayTaskRequest(InboundOrderId InboundOrderId, string TaskNo, string LineNo, string FromLocationCode, string ToLocationCode, decimal Quantity);
public sealed record ListWarehouseTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? OperatorUserId = null,
    string? Keyword = null);
public sealed record CreateWarehouseTaskResponse(WarehouseTaskId WarehouseTaskId);
public sealed record RecordWarehouseTaskProgressRequest(WarehouseTaskId WarehouseTaskId, decimal ExecutedQuantity);
public sealed record CompleteWarehouseTaskRequest(WarehouseTaskId WarehouseTaskId);
public sealed record CompleteInboundOrderRequest(InboundOrderId InboundOrderId, string IdempotencyKey);
public sealed record CompleteMovementResponse(InventoryMovementRequestId? RequestId, string? InventoryMovementId);
public sealed record RetryInboundInventoryPostingRequest(InboundOrderId InboundOrderId, string IdempotencyKey);
public sealed record CreateOutboundOrderRequest(string OrganizationId, string EnvironmentId, string OutboundOrderNo, string SourceDocumentType, string SourceDocumentId, string SiteCode, IReadOnlyCollection<WmsOutboundLineInput> Lines);
public sealed record CreateOutboundOrderResponse(OutboundOrderId OutboundOrderId);
public sealed record ListOutboundOrdersRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100, string? Status = null, string? Keyword = null);
public sealed record CreatePickingTaskRequest(OutboundOrderId OutboundOrderId, string TaskNo, string LineNo, string FromLocationCode, string ToLocationCode, decimal Quantity);
public sealed record CompleteOutboundOrderRequest(OutboundOrderId OutboundOrderId, string PackReviewNo, bool Passed, string IdempotencyKey);
public sealed record CancelOutboundOrderRequest(OutboundOrderId OutboundOrderId, string Reason);
public sealed record CancelOutboundOrderResponse(OutboundOrderId OutboundOrderId, string Status);
public sealed record RetryOutboundInventoryPostingRequest(OutboundOrderId OutboundOrderId, string IdempotencyKey);
public sealed record CreateCountExecutionRequest(string OrganizationId, string EnvironmentId, string CountNo, string SkuCode, string UomCode, string SiteCode, string LocationCode, decimal ExpectedQuantity);
public sealed record CreateCountExecutionResponse(CountExecutionId CountExecutionId);
public sealed record ListCountExecutionsRequest(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? Keyword = null);
public sealed record CompleteCountExecutionRequest(CountExecutionId CountExecutionId, decimal CountedQuantity, string IdempotencyKey);
public sealed record DispatchWcsTaskRequest(WarehouseTaskId WarehouseTaskId, string AdapterType, string ExternalTaskId, string PayloadJson);
public sealed record DispatchWcsTaskResponse(WcsTaskId WcsTaskId);
public sealed record CompleteWcsTaskRequest(string OrganizationId, string EnvironmentId, string ExternalTaskId, string CompletionPayloadJson);
public sealed record FailWcsTaskRequest(string OrganizationId, string EnvironmentId, string ExternalTaskId, string FailureCode, string FailureMessage);
public sealed record ListWcsTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ExternalTaskId = null,
    WarehouseTaskId? WarehouseTaskId = null,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    bool? Failed = null,
    string? Keyword = null);
public sealed record ListReceivingQualityGatesRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100, string? GateStatus = null, string? Keyword = null);
public sealed record ListSupplierReturnRequestsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100, string? Status = null, string? Keyword = null);

public sealed class CreateInboundOrderEndpoint(ISender sender) : WmsEndpoint<CreateInboundOrderRequest, ResponseData<CreateInboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateInboundOrderEndpoint>());
    public override async Task HandleAsync(CreateInboundOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateInboundOrderCommand(req.OrganizationId, req.EnvironmentId, req.InboundOrderNo, req.SourceDocumentType, req.SourceDocumentId, req.SiteCode, req.Lines), ct);
        await Send.OkAsync(new CreateInboundOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListInboundOrdersEndpoint(ISender sender) : WmsEndpoint<ListInboundOrdersRequest, ResponseData<ListInboundOrdersResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListInboundOrdersEndpoint>());
    public override async Task HandleAsync(ListInboundOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListInboundOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.Status, req.Keyword), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreatePutawayTaskEndpoint(ISender sender) : WmsEndpoint<CreatePutawayTaskRequest, ResponseData<CreateWarehouseTaskResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreatePutawayTaskEndpoint>());
    public override async Task HandleAsync(CreatePutawayTaskRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreatePutawayTaskCommand(req.InboundOrderId, req.TaskNo, req.LineNo, req.FromLocationCode, req.ToLocationCode, req.Quantity), ct);
        await Send.OkAsync(new CreateWarehouseTaskResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPutawayTasksEndpoint(ISender sender) : WmsEndpoint<ListWarehouseTasksRequest, ResponseData<ListWarehouseTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListPutawayTasksEndpoint>());
    public override async Task HandleAsync(ListWarehouseTasksRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWarehouseTasksQuery(req.OrganizationId, req.EnvironmentId, WarehouseTaskType.Putaway, req.Skip, req.Take, req.Status, req.LocationCode, req.OperatorUserId, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteInboundOrderEndpoint(ISender sender) : WmsEndpoint<CompleteInboundOrderRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteInboundOrderEndpoint>());
    public override async Task HandleAsync(CompleteInboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteInboundOrderCommand(req.InboundOrderId, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class RetryInboundInventoryPostingEndpoint(ISender sender) : WmsEndpoint<RetryInboundInventoryPostingRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<RetryInboundInventoryPostingEndpoint>());
    public override async Task HandleAsync(RetryInboundInventoryPostingRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RetryInboundInventoryPostingCommand(req.InboundOrderId, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CreateOutboundOrderRequest, ResponseData<CreateOutboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateOutboundOrderEndpoint>());
    public override async Task HandleAsync(CreateOutboundOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOutboundOrderCommand(req.OrganizationId, req.EnvironmentId, req.OutboundOrderNo, req.SourceDocumentType, req.SourceDocumentId, req.SiteCode, req.Lines), ct);
        await Send.OkAsync(new CreateOutboundOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListOutboundOrdersEndpoint(ISender sender) : WmsEndpoint<ListOutboundOrdersRequest, ResponseData<ListOutboundOrdersResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListOutboundOrdersEndpoint>());
    public override async Task HandleAsync(ListOutboundOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListOutboundOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.Status, req.Keyword), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreatePickingTaskEndpoint(ISender sender) : WmsEndpoint<CreatePickingTaskRequest, ResponseData<CreateWarehouseTaskResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreatePickingTaskEndpoint>());
    public override async Task HandleAsync(CreatePickingTaskRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreatePickingTaskCommand(req.OutboundOrderId, req.TaskNo, req.LineNo, req.FromLocationCode, req.ToLocationCode, req.Quantity), ct);
        await Send.OkAsync(new CreateWarehouseTaskResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPickingTasksEndpoint(ISender sender) : WmsEndpoint<ListWarehouseTasksRequest, ResponseData<ListWarehouseTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListPickingTasksEndpoint>());
    public override async Task HandleAsync(ListWarehouseTasksRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWarehouseTasksQuery(req.OrganizationId, req.EnvironmentId, WarehouseTaskType.Picking, req.Skip, req.Take, req.Status, req.LocationCode, req.OperatorUserId, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordWarehouseTaskProgressEndpoint(ISender sender) : WmsEndpoint<RecordWarehouseTaskProgressRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<RecordWarehouseTaskProgressEndpoint>());
    public override async Task HandleAsync(RecordWarehouseTaskProgressRequest req, CancellationToken ct)
    {
        await sender.Send(new RecordWarehouseTaskProgressCommand(req.WarehouseTaskId, req.ExecutedQuantity), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteWarehouseTaskEndpoint(ISender sender) : WmsEndpoint<CompleteWarehouseTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteWarehouseTaskEndpoint>());
    public override async Task HandleAsync(CompleteWarehouseTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteWarehouseTaskCommand(req.WarehouseTaskId), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CompleteOutboundOrderRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteOutboundOrderEndpoint>());
    public override async Task HandleAsync(CompleteOutboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteOutboundOrderCommand(req.OutboundOrderId, req.PackReviewNo, req.Passed, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class CancelOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CancelOutboundOrderRequest, ResponseData<CancelOutboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CancelOutboundOrderEndpoint>());
    public override async Task HandleAsync(CancelOutboundOrderRequest req, CancellationToken ct)
    {
        await sender.Send(new CancelOutboundOrderCommand(req.OutboundOrderId, req.Reason), ct);
        await Send.OkAsync(new CancelOutboundOrderResponse(req.OutboundOrderId, "Cancelled").AsResponseData(), cancellation: ct);
    }
}

public sealed class RetryOutboundInventoryPostingEndpoint(ISender sender) : WmsEndpoint<RetryOutboundInventoryPostingRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<RetryOutboundInventoryPostingEndpoint>());
    public override async Task HandleAsync(RetryOutboundInventoryPostingRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RetryOutboundInventoryPostingCommand(req.OutboundOrderId, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateCountExecutionEndpoint(ISender sender) : WmsEndpoint<CreateCountExecutionRequest, ResponseData<CreateCountExecutionResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateCountExecutionEndpoint>());
    public override async Task HandleAsync(CreateCountExecutionRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateCountExecutionCommand(req.OrganizationId, req.EnvironmentId, req.CountNo, req.SkuCode, req.UomCode, req.SiteCode, req.LocationCode, req.ExpectedQuantity), ct);
        await Send.OkAsync(new CreateCountExecutionResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListCountExecutionsEndpoint(ISender sender) : WmsEndpoint<ListCountExecutionsRequest, ResponseData<ListCountExecutionsResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListCountExecutionsEndpoint>());
    public override async Task HandleAsync(ListCountExecutionsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListCountExecutionsQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.Status, req.LocationCode, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteCountExecutionEndpoint(ISender sender) : WmsEndpoint<CompleteCountExecutionRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteCountExecutionEndpoint>());
    public override async Task HandleAsync(CompleteCountExecutionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteCountExecutionCommand(req.CountExecutionId, req.CountedQuantity, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class DispatchWcsTaskEndpoint(ISender sender) : WmsEndpoint<DispatchWcsTaskRequest, ResponseData<DispatchWcsTaskResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<DispatchWcsTaskEndpoint>());
    public override async Task HandleAsync(DispatchWcsTaskRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new DispatchWcsTaskCommand(req.WarehouseTaskId, req.AdapterType, req.ExternalTaskId, req.PayloadJson), ct);
        await Send.OkAsync(new DispatchWcsTaskResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteWcsTaskEndpoint(ISender sender) : WmsEndpoint<CompleteWcsTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteWcsTaskEndpoint>());
    public override async Task HandleAsync(CompleteWcsTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteWcsTaskCommand(req.OrganizationId, req.EnvironmentId, req.ExternalTaskId, req.CompletionPayloadJson), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class FailWcsTaskEndpoint(ISender sender) : WmsEndpoint<FailWcsTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<FailWcsTaskEndpoint>());
    public override async Task HandleAsync(FailWcsTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new FailWcsTaskCommand(req.OrganizationId, req.EnvironmentId, req.ExternalTaskId, req.FailureCode, req.FailureMessage), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWcsTasksEndpoint(ISender sender) : WmsEndpoint<ListWcsTasksRequest, ResponseData<ListWcsTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListWcsTasksEndpoint>());
    public override async Task HandleAsync(ListWcsTasksRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWcsTasksQuery(req.OrganizationId, req.EnvironmentId, req.ExternalTaskId, req.WarehouseTaskId, req.Skip, req.Take, req.Status, req.Failed, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListReceivingQualityGatesEndpoint(ISender sender) : WmsEndpoint<ListReceivingQualityGatesRequest, ResponseData<ListReceivingQualityGatesResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListReceivingQualityGatesEndpoint>());
    public override async Task HandleAsync(ListReceivingQualityGatesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListReceivingQualityGatesQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.GateStatus, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListSupplierReturnRequestsEndpoint(ISender sender) : WmsEndpoint<ListSupplierReturnRequestsRequest, ResponseData<ListSupplierReturnRequestsResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListSupplierReturnRequestsEndpoint>());
    public override async Task HandleAsync(ListSupplierReturnRequestsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListSupplierReturnRequestsQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.Status, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record WmsEndpointContract(Type EndpointType, string HttpMethod, string Route, string PermissionCode, string AuthorizationPolicy, string OperationId);

public static class WmsEndpointContracts
{
    public static readonly IReadOnlyCollection<WmsEndpointContract> All =
    [
        new(typeof(CreateInboundOrderEndpoint), "POST", "/api/business/v1/wms/inbound-orders", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsInboundOrder"),
        new(typeof(ListInboundOrdersEndpoint), "GET", "/api/business/v1/wms/inbound-orders", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsInboundOrders"),
        new(typeof(CreatePutawayTaskEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsPutawayTask"),
        new(typeof(ListPutawayTasksEndpoint), "GET", "/api/business/v1/wms/putaway-tasks", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsPutawayTasks"),
        new(typeof(CompleteInboundOrderEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsInboundOrder"),
        new(typeof(RetryInboundInventoryPostingEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/inventory-posting/retry", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "retryWmsInboundInventoryPosting"),
        new(typeof(CreateOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "createWmsOutboundOrder"),
        new(typeof(ListOutboundOrdersEndpoint), "GET", "/api/business/v1/wms/outbound-orders", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsOutboundOrders"),
        new(typeof(CreatePickingTaskEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "createWmsPickingTask"),
        new(typeof(ListPickingTasksEndpoint), "GET", "/api/business/v1/wms/picking-tasks", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsPickingTasks"),
        new(typeof(RecordWarehouseTaskProgressEndpoint), "POST", "/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "recordWmsWarehouseTaskProgress"),
        new(typeof(CompleteWarehouseTaskEndpoint), "POST", "/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsWarehouseTask"),
        new(typeof(CompleteOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsOutboundOrder"),
        new(typeof(CancelOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/cancel", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "cancelWmsOutboundOrder"),
        new(typeof(RetryOutboundInventoryPostingEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "retryWmsOutboundInventoryPosting"),
        new(typeof(CreateCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsCountExecution"),
        new(typeof(ListCountExecutionsEndpoint), "GET", "/api/business/v1/wms/count-executions", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsCountExecutions"),
        new(typeof(CompleteCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions/{countExecutionId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsCountExecution"),
        new(typeof(DispatchWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "dispatchWmsWcsTask"),
        new(typeof(CompleteWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "completeWmsWcsTask"),
        new(typeof(FailWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "failWmsWcsTask"),
        new(typeof(ListWcsTasksEndpoint), "GET", "/api/business/v1/wms/wcs-tasks", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "listWmsWcsTasks"),
        new(typeof(ListReceivingQualityGatesEndpoint), "GET", "/api/business/v1/wms/receiving-quality-gates", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsReceivingQualityGates"),
        new(typeof(ListSupplierReturnRequestsEndpoint), "GET", "/api/business/v1/wms/supplier-return-requests", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsSupplierReturnRequests"),
    ];

    public static WmsEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out WmsEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
