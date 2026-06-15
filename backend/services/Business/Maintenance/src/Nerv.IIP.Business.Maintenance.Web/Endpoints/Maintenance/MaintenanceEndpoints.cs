using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Auth;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;
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

public sealed record ListMaintenanceWorkOrdersRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed record CreateMaintenancePlanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? PlanCode,
    string Interval,
    DateOnly StartsOn,
    string Owner,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    string? IdempotencyKey = null);

public sealed record CreateMaintenancePlanResponse(MaintenancePlanId PlanId);

public sealed record ListMaintenancePlansRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed record GetMaintenanceAssetAvailabilityWindowsRequest(
    string DeviceAssetId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FreshnessMaxAgeMinutes = 60);

public sealed record QueryMaintenanceAvailabilityWindowsRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetIds,
    string? WorkCenterIds,
    int FreshnessMaxAgeMinutes = 60);

public sealed record RecordMaintenanceInspectionRequest(
    string OrganizationId,
    string EnvironmentId,
    MaintenancePlanId? PlanId,
    MaintenanceWorkOrderId? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc);

public sealed record RecordMaintenanceInspectionResponse(MaintenanceInspectionId InspectionId);

public sealed record ListMaintenanceInspectionsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed record ListMaintenanceSparePartsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed record CreateMaintenanceSparePartRequest(
    string OrganizationId,
    string EnvironmentId,
    MaintenanceWorkOrderId WorkOrderId,
    string SkuCode,
    decimal Quantity,
    string? UomCode);

public sealed record CreateMaintenanceSparePartResponse(SparePartLineId SparePartLineId);

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
    : MaintenanceEndpoint<ListMaintenanceWorkOrdersRequest, ResponseData<PagedMaintenanceListResponse<MaintenanceWorkOrderListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenanceWorkOrdersEndpoint>());

    public override async Task HandleAsync(ListMaintenanceWorkOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenanceWorkOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateMaintenancePlanEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateMaintenancePlanRequest, ResponseData<CreateMaintenancePlanResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateMaintenancePlanEndpoint>());

    public override async Task HandleAsync(CreateMaintenancePlanRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMaintenancePlanCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.PlanCode, req.Interval, req.StartsOn, req.Owner, req.WindowStartUtc, req.WindowEndUtc, req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateMaintenancePlanResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMaintenancePlansEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenancePlansRequest, ResponseData<PagedMaintenanceListResponse<MaintenancePlanListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenancePlansEndpoint>());

    public override async Task HandleAsync(ListMaintenancePlansRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenancePlansQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetMaintenanceAssetAvailabilityWindowsEndpoint(ISender sender)
    : MaintenanceEndpoint<GetMaintenanceAssetAvailabilityWindowsRequest, ResponseData<EquipmentRuntimeAvailabilityResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<GetMaintenanceAssetAvailabilityWindowsEndpoint>());

    public override async Task HandleAsync(GetMaintenanceAssetAvailabilityWindowsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetMaintenanceAssetAvailabilityWindowsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.WindowStartUtc, req.WindowEndUtc, req.FreshnessMaxAgeMinutes), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryMaintenanceAvailabilityWindowsEndpoint(ISender sender)
    : MaintenanceEndpoint<QueryMaintenanceAvailabilityWindowsRequest, ResponseData<EquipmentRuntimeAvailabilityResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<QueryMaintenanceAvailabilityWindowsEndpoint>());

    public override async Task HandleAsync(QueryMaintenanceAvailabilityWindowsRequest req, CancellationToken ct)
    {
        var request = new EquipmentRuntimeAvailabilityRequest(
            req.OrganizationId,
            req.EnvironmentId,
            req.WindowStartUtc,
            req.WindowEndUtc,
            SplitCsv(req.DeviceAssetIds),
            SplitCsv(req.WorkCenterIds),
            req.FreshnessMaxAgeMinutes);
        var result = await sender.Send(new QueryMaintenanceAvailabilityWindowsQuery(request), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }

    private static string[]? SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

public sealed class ListMaintenanceInspectionsEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenanceInspectionsRequest, ResponseData<PagedMaintenanceListResponse<MaintenanceInspectionListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenanceInspectionsEndpoint>());

    public override async Task HandleAsync(ListMaintenanceInspectionsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenanceInspectionsQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMaintenanceSparePartsEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenanceSparePartsRequest, ResponseData<PagedMaintenanceListResponse<MaintenanceSparePartListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenanceSparePartsEndpoint>());

    public override async Task HandleAsync(ListMaintenanceSparePartsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenanceSparePartsQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateMaintenanceSparePartEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateMaintenanceSparePartRequest, ResponseData<CreateMaintenanceSparePartResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateMaintenanceSparePartEndpoint>());

    public override async Task HandleAsync(CreateMaintenanceSparePartRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMaintenanceSparePartCommand(req.OrganizationId, req.EnvironmentId, req.WorkOrderId, req.SkuCode, req.Quantity, req.UomCode), ct);
        await Send.OkAsync(new CreateMaintenanceSparePartResponse(id).AsResponseData(), cancellation: ct);
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
        new(typeof(ListMaintenanceInspectionsEndpoint), "GET", "/api/business/v1/maintenance/inspections", MaintenancePermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceInspections"),
        new(typeof(ListMaintenanceSparePartsEndpoint), "GET", "/api/business/v1/maintenance/spare-parts", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceSpareParts"),
        new(typeof(CreateMaintenanceSparePartEndpoint), "POST", "/api/business/v1/maintenance/spare-parts", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "createMaintenanceSparePart"),
        new(typeof(GetMaintenanceAssetAvailabilityWindowsEndpoint), "GET", "/api/business/v1/maintenance/assets/{deviceAssetId}/availability-windows", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "getMaintenanceAssetAvailabilityWindows"),
        new(typeof(QueryMaintenanceAvailabilityWindowsEndpoint), "GET", "/api/business/v1/maintenance/availability-windows", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "queryMaintenanceAvailabilityWindows"),
    ];

    public static MaintenanceEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out MaintenanceEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
