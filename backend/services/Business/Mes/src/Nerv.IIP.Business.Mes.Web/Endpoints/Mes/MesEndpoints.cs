using FastEndpoints;
using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.ServiceAuth;
using System.Diagnostics.CodeAnalysis;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

public sealed record RunScheduleRequest(
    string OrganizationId,
    string EnvironmentId,
    RescheduleTrigger Trigger);

public sealed record CreateRushWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string? OperationTaskId,
    int? OperationSequence,
    int DurationMinutes);

public sealed record ListMesWorkOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Take = 100);

public sealed record RecordProductionReportRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    bool CompletesOperation,
    DateTimeOffset ReportedAtUtc);

public sealed record RecordProductionReportResponse(global::Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate.ProductionReportId ProductionReportId);

public sealed record ListProductionReportsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Take = 100);

public sealed record CreateFinishedGoodsReceiptRequestRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc);

public sealed record CreateFinishedGoodsReceiptRequestResponse(global::Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequestId FinishedGoodsReceiptRequestId);

public sealed record ListFinishedGoodsReceiptRequestsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Take = 100);

public sealed record ListCapacityImpactsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    int Take = 100);

public abstract class MesEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureMesContract(MesEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by MES endpoints.");
        }

        Tags("Business MES");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }
}

public sealed class RunScheduleEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<RunScheduleRequest, MesScheduleResult>
{
    public override void Configure()
    {
        ConfigureMesContract(MesEndpointContracts.Get<RunScheduleEndpoint>());
    }

    public override async Task HandleAsync(RunScheduleRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RescheduleCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Trigger,
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(result, ct);
    }
}

public sealed class CreateRushWorkOrderEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<CreateRushWorkOrderRequest, CreateRushWorkOrderResponse>
{
    public override void Configure()
    {
        ConfigureMesContract(MesEndpointContracts.Get<CreateRushWorkOrderEndpoint>());
    }

    public override async Task HandleAsync(CreateRushWorkOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateRushWorkOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.SkuId,
            req.ProductionVersionId,
            req.Quantity,
            req.DueUtc,
            req.WorkCenterId,
            req.OperationTaskId ?? $"{req.WorkOrderId}-OP-10",
            req.OperationSequence ?? 10,
            TimeSpan.FromMinutes(req.DurationMinutes),
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(result, ct);
    }
}

public sealed class ListMesWorkOrdersEndpoint(ISender sender)
    : MesEndpoint<ListMesWorkOrdersRequest, ListMesWorkOrdersResponse>
{
    public override void Configure()
    {
        ConfigureMesContract(MesEndpointContracts.Get<ListMesWorkOrdersEndpoint>());
    }

    public override async Task HandleAsync(ListMesWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ListMesWorkOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Take),
            ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class RecordProductionReportEndpoint(ISender sender)
    : MesEndpoint<RecordProductionReportRequest, RecordProductionReportResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<RecordProductionReportEndpoint>());

    public override async Task HandleAsync(RecordProductionReportRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new RecordProductionReportCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.OperationTaskId,
            req.GoodQuantity,
            req.ScrapQuantity,
            req.CompletesOperation,
            req.ReportedAtUtc), ct);
        await Send.OkAsync(new RecordProductionReportResponse(id), ct);
    }
}

public sealed class ListProductionReportsEndpoint(ISender sender)
    : MesEndpoint<ListProductionReportsRequest, ListProductionReportsResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListProductionReportsEndpoint>());

    public override async Task HandleAsync(ListProductionReportsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListProductionReportsQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class CreateFinishedGoodsReceiptRequestEndpoint(ISender sender)
    : MesEndpoint<CreateFinishedGoodsReceiptRequestRequest, CreateFinishedGoodsReceiptRequestResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<CreateFinishedGoodsReceiptRequestEndpoint>());

    public override async Task HandleAsync(CreateFinishedGoodsReceiptRequestRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateFinishedGoodsReceiptRequestCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.SkuId,
            req.Quantity,
            req.UomCode,
            req.RequestedAtUtc), ct);
        await Send.OkAsync(new CreateFinishedGoodsReceiptRequestResponse(id), ct);
    }
}

public sealed class ListFinishedGoodsReceiptRequestsEndpoint(ISender sender)
    : MesEndpoint<ListFinishedGoodsReceiptRequestsRequest, ListFinishedGoodsReceiptRequestsResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListFinishedGoodsReceiptRequestsEndpoint>());

    public override async Task HandleAsync(ListFinishedGoodsReceiptRequestsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListFinishedGoodsReceiptRequestsQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListCapacityImpactsEndpoint(ISender sender)
    : MesEndpoint<ListCapacityImpactsRequest, ListCapacityImpactsResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListCapacityImpactsEndpoint>());

    public override async Task HandleAsync(ListCapacityImpactsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListCapacityImpactsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed record MesEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class MesEndpointContracts
{
    public static readonly IReadOnlyCollection<MesEndpointContract> All =
    [
        new(typeof(RunScheduleEndpoint), "POST", "/api/business/v1/mes/schedules/run", MesPermissionCodes.SchedulesManage, "runBusinessMesSchedule"),
        new(typeof(CreateRushWorkOrderEndpoint), "POST", "/api/business/v1/mes/work-orders/rush", MesPermissionCodes.WorkOrdersManage, "createBusinessMesRushWorkOrder"),
        new(typeof(ListMesWorkOrdersEndpoint), "GET", "/api/business/v1/mes/work-orders", MesPermissionCodes.WorkOrdersManage, "listBusinessMesWorkOrders"),
        new(typeof(RecordProductionReportEndpoint), "POST", "/api/business/v1/mes/production-reports", MesPermissionCodes.WorkOrdersManage, "recordBusinessMesProductionReport"),
        new(typeof(ListProductionReportsEndpoint), "GET", "/api/business/v1/mes/production-reports", MesPermissionCodes.WorkOrdersManage, "listBusinessMesProductionReports"),
        new(typeof(CreateFinishedGoodsReceiptRequestEndpoint), "POST", "/api/business/v1/mes/finished-goods-receipt-requests", MesPermissionCodes.WorkOrdersManage, "createBusinessMesFinishedGoodsReceiptRequest"),
        new(typeof(ListFinishedGoodsReceiptRequestsEndpoint), "GET", "/api/business/v1/mes/finished-goods-receipt-requests", MesPermissionCodes.WorkOrdersManage, "listBusinessMesFinishedGoodsReceiptRequests"),
        new(typeof(ListCapacityImpactsEndpoint), "GET", "/api/business/v1/mes/capacity-impacts", MesPermissionCodes.SchedulesManage, "listBusinessMesCapacityImpacts"),
    ];

    public static MesEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out MesEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
