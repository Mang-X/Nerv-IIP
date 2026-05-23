using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Auth;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;

public abstract class MaintenanceEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureMaintenanceContract(MaintenanceEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by Maintenance endpoints.");
        }

        Tags("Business Maintenance");
        Policies(contract.AuthorizationPolicy);
        Permissions(contract.PermissionCode);
    }
}

public sealed record CreateMaintenanceWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string Priority,
    string? SourceAlarmId,
    string OpenedBy,
    string? AssetUnavailableReason);

public sealed record CreateMaintenanceWorkOrderResponse(MaintenanceWorkOrderId WorkOrderId);

public sealed record CompleteMaintenanceWorkOrderRequest(
    MaintenanceWorkOrderId WorkOrderId,
    string Result,
    string DowntimeReasonCode,
    int DowntimeMinutes,
    IReadOnlyCollection<MaintenanceSparePartInput> SpareParts);

public sealed record ListMaintenanceWorkOrdersRequest(string? OrganizationId, string? EnvironmentId);

public sealed record CreateMaintenancePlanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string PlanCode,
    string Interval,
    DateOnly StartsOn,
    string Owner);

public sealed record CreateMaintenancePlanResponse(MaintenancePlanId PlanId);

public sealed record ListMaintenancePlansRequest(string? OrganizationId, string? EnvironmentId);

public sealed record RecordMaintenanceInspectionRequest(
    string OrganizationId,
    string EnvironmentId,
    MaintenancePlanId? PlanId,
    MaintenanceWorkOrderId? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc);

public sealed record RecordMaintenanceInspectionResponse(MaintenanceInspectionId InspectionId);

public sealed class CreateMaintenanceWorkOrderEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateMaintenanceWorkOrderRequest, ResponseData<CreateMaintenanceWorkOrderResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateMaintenanceWorkOrderEndpoint>());

    public override async Task HandleAsync(CreateMaintenanceWorkOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMaintenanceWorkOrderCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Priority, req.SourceAlarmId, req.OpenedBy, req.AssetUnavailableReason), ct);
        await Send.OkAsync(new CreateMaintenanceWorkOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteMaintenanceWorkOrderEndpoint(ISender sender)
    : MaintenanceEndpoint<CompleteMaintenanceWorkOrderRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CompleteMaintenanceWorkOrderEndpoint>());

    public override async Task HandleAsync(CompleteMaintenanceWorkOrderRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteMaintenanceWorkOrderCommand(req.WorkOrderId, req.Result, req.DowntimeReasonCode, req.DowntimeMinutes, req.SpareParts), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMaintenanceWorkOrdersEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenanceWorkOrdersRequest, ResponseData<IReadOnlyCollection<MaintenanceWorkOrderListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenanceWorkOrdersEndpoint>());

    public override async Task HandleAsync(ListMaintenanceWorkOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenanceWorkOrdersQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateMaintenancePlanEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateMaintenancePlanRequest, ResponseData<CreateMaintenancePlanResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateMaintenancePlanEndpoint>());

    public override async Task HandleAsync(CreateMaintenancePlanRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMaintenancePlanCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.PlanCode, req.Interval, req.StartsOn, req.Owner), ct);
        await Send.OkAsync(new CreateMaintenancePlanResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMaintenancePlansEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenancePlansRequest, ResponseData<IReadOnlyCollection<MaintenancePlanListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenancePlansEndpoint>());

    public override async Task HandleAsync(ListMaintenancePlansRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenancePlansQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordMaintenanceInspectionEndpoint(ISender sender)
    : MaintenanceEndpoint<RecordMaintenanceInspectionRequest, ResponseData<RecordMaintenanceInspectionResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<RecordMaintenanceInspectionEndpoint>());

    public override async Task HandleAsync(RecordMaintenanceInspectionRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new RecordMaintenanceInspectionCommand(req.OrganizationId, req.EnvironmentId, req.PlanId, req.WorkOrderId, req.Inspector, req.Result, req.InspectedAtUtc), ct);
        await Send.OkAsync(new RecordMaintenanceInspectionResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed record MaintenanceEndpointContract(Type EndpointType, string HttpMethod, string Route, string PermissionCode, string AuthorizationPolicy, string OperationId);

public static class MaintenanceEndpointContracts
{
    public static readonly IReadOnlyCollection<MaintenanceEndpointContract> All =
    [
        new(typeof(CreateMaintenanceWorkOrderEndpoint), "POST", "/api/business/v1/maintenance/work-orders", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "createMaintenanceWorkOrder"),
        new(typeof(CompleteMaintenanceWorkOrderEndpoint), "POST", "/api/business/v1/maintenance/work-orders/{workOrderId}/complete", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "completeMaintenanceWorkOrder"),
        new(typeof(ListMaintenanceWorkOrdersEndpoint), "GET", "/api/business/v1/maintenance/work-orders", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceWorkOrders"),
        new(typeof(CreateMaintenancePlanEndpoint), "POST", "/api/business/v1/maintenance/plans", MaintenancePermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "createMaintenancePlan"),
        new(typeof(ListMaintenancePlansEndpoint), "GET", "/api/business/v1/maintenance/plans", MaintenancePermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listMaintenancePlans"),
        new(typeof(RecordMaintenanceInspectionEndpoint), "POST", "/api/business/v1/maintenance/inspections", MaintenancePermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "recordMaintenanceInspection"),
    ];

    public static MaintenanceEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out MaintenanceEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
