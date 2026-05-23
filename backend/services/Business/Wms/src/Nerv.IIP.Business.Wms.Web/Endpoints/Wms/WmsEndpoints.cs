using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
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
        Permissions(contract.PermissionCode);
    }
}

public sealed record CreateInboundOrderRequest(string OrganizationId, string EnvironmentId, string InboundOrderNo, string SourceDocumentType, string SourceDocumentId, string SiteCode, IReadOnlyCollection<WmsInboundLineInput> Lines);
public sealed record CreateInboundOrderResponse(InboundOrderId InboundOrderId);
public sealed record ListInboundOrdersRequest(string? OrganizationId, string? EnvironmentId);
public sealed record CreatePutawayTaskRequest(InboundOrderId InboundOrderId, string TaskNo, string LineNo, string FromLocationCode, string ToLocationCode, decimal Quantity);
public sealed record CreateWarehouseTaskResponse(WarehouseTaskId WarehouseTaskId);
public sealed record CompleteInboundOrderRequest(InboundOrderId InboundOrderId, string IdempotencyKey);
public sealed record CompleteMovementResponse(string InventoryMovementId);
public sealed record CreateOutboundOrderRequest(string OrganizationId, string EnvironmentId, string OutboundOrderNo, string SourceDocumentType, string SourceDocumentId, string SiteCode, IReadOnlyCollection<WmsOutboundLineInput> Lines);
public sealed record CreateOutboundOrderResponse(OutboundOrderId OutboundOrderId);
public sealed record ListOutboundOrdersRequest(string? OrganizationId, string? EnvironmentId);
public sealed record CreatePickingTaskRequest(OutboundOrderId OutboundOrderId, string TaskNo, string LineNo, string FromLocationCode, string ToLocationCode, decimal Quantity);
public sealed record CompleteOutboundOrderRequest(OutboundOrderId OutboundOrderId, string PackReviewNo, bool Passed, string IdempotencyKey);
public sealed record CreateCountExecutionRequest(string OrganizationId, string EnvironmentId, string CountNo, string SkuCode, string UomCode, string SiteCode, string LocationCode, decimal ExpectedQuantity);
public sealed record CreateCountExecutionResponse(CountExecutionId CountExecutionId);
public sealed record CompleteCountExecutionRequest(CountExecutionId CountExecutionId, decimal CountedQuantity);
public sealed record DispatchWcsTaskRequest(WarehouseTaskId WarehouseTaskId, string AdapterType, string ExternalTaskId, string PayloadJson);
public sealed record DispatchWcsTaskResponse(WcsTaskId WcsTaskId);
public sealed record CompleteWcsTaskRequest(string ExternalTaskId, string CompletionPayloadJson);
public sealed record FailWcsTaskRequest(string ExternalTaskId, string FailureCode, string FailureMessage);

public sealed class CreateInboundOrderEndpoint(ISender sender) : WmsEndpoint<CreateInboundOrderRequest, ResponseData<CreateInboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateInboundOrderEndpoint>());
    public override async Task HandleAsync(CreateInboundOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateInboundOrderCommand(req.OrganizationId, req.EnvironmentId, req.InboundOrderNo, req.SourceDocumentType, req.SourceDocumentId, req.SiteCode, req.Lines), ct);
        await Send.OkAsync(new CreateInboundOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListInboundOrdersEndpoint(ISender sender) : WmsEndpoint<ListInboundOrdersRequest, ResponseData<IReadOnlyCollection<InboundOrderListItem>>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListInboundOrdersEndpoint>());
    public override async Task HandleAsync(ListInboundOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListInboundOrdersQuery(req.OrganizationId, req.EnvironmentId), ct);
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

public sealed class CompleteInboundOrderEndpoint(ISender sender) : WmsEndpoint<CompleteInboundOrderRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteInboundOrderEndpoint>());
    public override async Task HandleAsync(CompleteInboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteInboundOrderCommand(req.InboundOrderId, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.InventoryMovementId).AsResponseData(), cancellation: ct);
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

public sealed class ListOutboundOrdersEndpoint(ISender sender) : WmsEndpoint<ListOutboundOrdersRequest, ResponseData<IReadOnlyCollection<OutboundOrderListItem>>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListOutboundOrdersEndpoint>());
    public override async Task HandleAsync(ListOutboundOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListOutboundOrdersQuery(req.OrganizationId, req.EnvironmentId), ct);
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

public sealed class CompleteOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CompleteOutboundOrderRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteOutboundOrderEndpoint>());
    public override async Task HandleAsync(CompleteOutboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteOutboundOrderCommand(req.OutboundOrderId, req.PackReviewNo, req.Passed, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.InventoryMovementId).AsResponseData(), cancellation: ct);
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

public sealed class CompleteCountExecutionEndpoint(ISender sender) : WmsEndpoint<CompleteCountExecutionRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteCountExecutionEndpoint>());
    public override async Task HandleAsync(CompleteCountExecutionRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteCountExecutionCommand(req.CountExecutionId, req.CountedQuantity), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
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
        await sender.Send(new CompleteWcsTaskCommand(req.ExternalTaskId, req.CompletionPayloadJson), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class FailWcsTaskEndpoint(ISender sender) : WmsEndpoint<FailWcsTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<FailWcsTaskEndpoint>());
    public override async Task HandleAsync(FailWcsTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new FailWcsTaskCommand(req.ExternalTaskId, req.FailureCode, req.FailureMessage), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
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
        new(typeof(CompleteInboundOrderEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsInboundOrder"),
        new(typeof(CreateOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "createWmsOutboundOrder"),
        new(typeof(ListOutboundOrdersEndpoint), "GET", "/api/business/v1/wms/outbound-orders", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsOutboundOrders"),
        new(typeof(CreatePickingTaskEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "createWmsPickingTask"),
        new(typeof(CompleteOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsOutboundOrder"),
        new(typeof(CreateCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsCountExecution"),
        new(typeof(CompleteCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions/{countExecutionId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsCountExecution"),
        new(typeof(DispatchWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "dispatchWmsWcsTask"),
        new(typeof(CompleteWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "completeWmsWcsTask"),
        new(typeof(FailWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "failWmsWcsTask"),
    ];

    public static WmsEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out WmsEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
