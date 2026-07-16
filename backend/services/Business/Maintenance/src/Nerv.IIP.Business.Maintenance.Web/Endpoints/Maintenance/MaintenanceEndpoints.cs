using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
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
            case "PUT":
                Put(contract.Route);
                break;
            case "DELETE":
                Delete(contract.Route);
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
    string? AssetUnavailableReason,
    string? AssignedTechnicianUserId = null,
    int? EstimatedLaborMinutes = null);

public sealed record CreateMaintenanceWorkOrderResponse(MaintenanceWorkOrderId WorkOrderId);

public sealed record CompleteMaintenanceWorkOrderRequest(
    MaintenanceWorkOrderId WorkOrderId,
    string Result,
    string DowntimeReasonCode,
    int DowntimeMinutes,
    IReadOnlyCollection<MaintenanceSparePartInput> SpareParts,
    int? ActualLaborMinutes = null,
    decimal? SparePartCostAmount = null,
    decimal? ExternalServiceCostAmount = null,
    string? CostCurrencyCode = null,
    string? ActualTechnicianUserId = null);

public sealed record StartMaintenanceRepairRequest(
    MaintenanceWorkOrderId WorkOrderId,
    DateTimeOffset RepairStartedAtUtc);

public sealed record ListMaintenanceWorkOrdersRequest(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? DeviceAssetIds = null);

public sealed record CreateMaintenancePlanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? PlanCode,
    // Nullable: a plan may be runtime-hour-only (no calendar interval). The command enforces
    // "at least one trigger" (calendar interval and/or runtime-hour interval).
    string? Interval,
    DateOnly StartsOn,
    string Owner,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    string? IdempotencyKey = null,
    decimal? RuntimeHourInterval = null);

public sealed record CreateMaintenancePlanResponse(MaintenancePlanId PlanId);

public sealed record ListMaintenancePlansRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100, string? DeviceAssetId = null);

public sealed record GenerateDueMaintenanceWorkOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly BusinessDate,
    string OpenedBy);

public sealed record QueryMaintenanceAssetReliabilityRequest(
    string DeviceAssetId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

public sealed record QueryMaintenanceReliabilitySummaryRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetId,
    string? TechnicianUserId);

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
    DateTimeOffset InspectedAtUtc,
    IReadOnlyCollection<MaintenanceInspectionMeasurementInput>? Measurements = null);

public sealed record RecordMaintenanceInspectionResponse(MaintenanceInspectionId InspectionId);

public sealed record ListMaintenanceInspectionsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed record QueryMaintenanceInspectionMeasurementTrendRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string CharacteristicCode,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

public sealed record ListMaintenanceSparePartsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed record CreateMaintenanceSparePartRequest(
    string OrganizationId,
    string EnvironmentId,
    MaintenanceWorkOrderId WorkOrderId,
    string SkuCode,
    decimal Quantity,
    string? UomCode);

public sealed record CreateMaintenanceSparePartResponse(SparePartLineId SparePartLineId);

public sealed record CreateDowntimeReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string Description,
    string ReasonCategory,
    string LossCategory);

public sealed record CreateDowntimeReasonResponse(DowntimeReasonId DowntimeReasonId);

public sealed record UpdateDowntimeReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string Description,
    string ReasonCategory,
    string LossCategory);

public sealed record DeleteDowntimeReasonRequest(string OrganizationId, string EnvironmentId, string ReasonCode);

public sealed record ListDowntimeReasonsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100);

public sealed class CreateMaintenanceWorkOrderEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateMaintenanceWorkOrderRequest, ResponseData<CreateMaintenanceWorkOrderResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateMaintenanceWorkOrderEndpoint>());

    public override async Task HandleAsync(CreateMaintenanceWorkOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMaintenanceWorkOrderCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Priority, req.SourceAlarmId, req.OpenedBy, req.AssetUnavailableReason, AssignedTechnicianUserId: req.AssignedTechnicianUserId, EstimatedLaborMinutes: req.EstimatedLaborMinutes), ct);
        await Send.OkAsync(new CreateMaintenanceWorkOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteMaintenanceWorkOrderEndpoint(ISender sender)
    : MaintenanceEndpoint<CompleteMaintenanceWorkOrderRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CompleteMaintenanceWorkOrderEndpoint>());

    public override async Task HandleAsync(CompleteMaintenanceWorkOrderRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteMaintenanceWorkOrderCommand(req.WorkOrderId, req.Result, req.DowntimeReasonCode, req.DowntimeMinutes, req.SpareParts, req.ActualLaborMinutes, req.SparePartCostAmount, req.ExternalServiceCostAmount, req.CostCurrencyCode, req.ActualTechnicianUserId), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class StartMaintenanceRepairEndpoint(ISender sender)
    : MaintenanceEndpoint<StartMaintenanceRepairRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<StartMaintenanceRepairEndpoint>());

    public override async Task HandleAsync(StartMaintenanceRepairRequest req, CancellationToken ct)
    {
        await sender.Send(new StartMaintenanceRepairCommand(req.WorkOrderId, req.RepairStartedAtUtc), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMaintenanceWorkOrdersEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenanceWorkOrdersRequest, ResponseData<PagedMaintenanceListResponse<MaintenanceWorkOrderListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenanceWorkOrdersEndpoint>());

    public override async Task HandleAsync(ListMaintenanceWorkOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenanceWorkOrdersQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Skip,
            req.Take,
            req.DeviceAssetIds), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateMaintenancePlanEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateMaintenancePlanRequest, ResponseData<CreateMaintenancePlanResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateMaintenancePlanEndpoint>());

    public override async Task HandleAsync(CreateMaintenancePlanRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMaintenancePlanCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.PlanCode, req.Interval, req.StartsOn, req.Owner, req.WindowStartUtc, req.WindowEndUtc, req.IdempotencyKey, req.RuntimeHourInterval), ct);
        await Send.OkAsync(new CreateMaintenancePlanResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMaintenancePlansEndpoint(ISender sender)
    : MaintenanceEndpoint<ListMaintenancePlansRequest, ResponseData<PagedMaintenanceListResponse<MaintenancePlanListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListMaintenancePlansEndpoint>());

    public override async Task HandleAsync(ListMaintenancePlansRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListMaintenancePlansQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.DeviceAssetId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class GenerateDueMaintenanceWorkOrdersEndpoint(ISender sender)
    : MaintenanceEndpoint<GenerateDueMaintenanceWorkOrdersRequest, ResponseData<GenerateDueMaintenanceWorkOrdersResult>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<GenerateDueMaintenanceWorkOrdersEndpoint>());

    public override async Task HandleAsync(GenerateDueMaintenanceWorkOrdersRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GenerateDueMaintenanceWorkOrdersCommand(req.OrganizationId, req.EnvironmentId, req.BusinessDate, req.OpenedBy), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryMaintenanceAssetReliabilityEndpoint(ISender sender)
    : MaintenanceEndpoint<QueryMaintenanceAssetReliabilityRequest, ResponseData<AssetReliabilityResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<QueryMaintenanceAssetReliabilityEndpoint>());

    public override async Task HandleAsync(QueryMaintenanceAssetReliabilityRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryAssetReliabilityQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.WindowStartUtc, req.WindowEndUtc), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryMaintenanceReliabilitySummaryEndpoint(ISender sender)
    : MaintenanceEndpoint<QueryMaintenanceReliabilitySummaryRequest, ResponseData<MaintenanceReliabilitySummaryResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<QueryMaintenanceReliabilitySummaryEndpoint>());

    public override async Task HandleAsync(QueryMaintenanceReliabilitySummaryRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryMaintenanceReliabilitySummaryQuery(req.OrganizationId, req.EnvironmentId, req.WindowStartUtc, req.WindowEndUtc, req.DeviceAssetId, req.TechnicianUserId), ct);
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
        var id = await sender.Send(new RecordMaintenanceInspectionCommand(req.OrganizationId, req.EnvironmentId, req.PlanId, req.WorkOrderId, req.Inspector, req.Result, req.InspectedAtUtc, req.Measurements), ct);
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

public sealed class QueryMaintenanceInspectionMeasurementTrendEndpoint(ISender sender)
    : MaintenanceEndpoint<QueryMaintenanceInspectionMeasurementTrendRequest, ResponseData<MaintenanceInspectionMeasurementTrendResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<QueryMaintenanceInspectionMeasurementTrendEndpoint>());

    public override async Task HandleAsync(QueryMaintenanceInspectionMeasurementTrendRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryMaintenanceInspectionMeasurementTrendQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.CharacteristicCode, req.WindowStartUtc, req.WindowEndUtc), ct);
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

public sealed class CreateDowntimeReasonEndpoint(ISender sender)
    : MaintenanceEndpoint<CreateDowntimeReasonRequest, ResponseData<CreateDowntimeReasonResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<CreateDowntimeReasonEndpoint>());

    public override async Task HandleAsync(CreateDowntimeReasonRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateDowntimeReasonCommand(req.OrganizationId, req.EnvironmentId, req.ReasonCode, req.Description, req.ReasonCategory, req.LossCategory), ct);
        await Send.OkAsync(new CreateDowntimeReasonResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListDowntimeReasonsEndpoint(ISender sender)
    : MaintenanceEndpoint<ListDowntimeReasonsRequest, ResponseData<PagedMaintenanceListResponse<DowntimeReasonListItem>>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<ListDowntimeReasonsEndpoint>());

    public override async Task HandleAsync(ListDowntimeReasonsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListDowntimeReasonsQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class UpdateDowntimeReasonEndpoint(ISender sender)
    : MaintenanceEndpoint<UpdateDowntimeReasonRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<UpdateDowntimeReasonEndpoint>());

    public override async Task HandleAsync(UpdateDowntimeReasonRequest req, CancellationToken ct)
    {
        await sender.Send(new UpdateDowntimeReasonCommand(req.OrganizationId, req.EnvironmentId, req.ReasonCode, req.Description, req.ReasonCategory, req.LossCategory), ct);
        await Send.OkAsync(new object().AsResponseData(), cancellation: ct);
    }
}

public sealed class DeleteDowntimeReasonEndpoint(ISender sender)
    : MaintenanceEndpoint<DeleteDowntimeReasonRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<DeleteDowntimeReasonEndpoint>());

    public override async Task HandleAsync(DeleteDowntimeReasonRequest req, CancellationToken ct)
    {
        await sender.Send(new DeleteDowntimeReasonCommand(req.OrganizationId, req.EnvironmentId, req.ReasonCode), ct);
        await Send.OkAsync(new object().AsResponseData(), cancellation: ct);
    }
}

public sealed record MaintenanceEndpointContract(Type EndpointType, string HttpMethod, string Route, string PermissionCode, string AuthorizationPolicy, string OperationId);

public static class MaintenanceEndpointContracts
{
    public static readonly IReadOnlyCollection<MaintenanceEndpointContract> All =
    [
        new(typeof(CreateMaintenanceWorkOrderEndpoint), "POST", "/api/business/v1/maintenance/work-orders", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "createMaintenanceWorkOrder"),
        new(typeof(StartMaintenanceRepairEndpoint), "POST", "/api/business/v1/maintenance/work-orders/{workOrderId}/repair-started", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "startMaintenanceRepair"),
        new(typeof(CompleteMaintenanceWorkOrderEndpoint), "POST", "/api/business/v1/maintenance/work-orders/{workOrderId}/complete", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "completeMaintenanceWorkOrder"),
        new(typeof(ListMaintenanceWorkOrdersEndpoint), "GET", "/api/business/v1/maintenance/work-orders", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceWorkOrders"),
        new(typeof(CreateMaintenancePlanEndpoint), "POST", "/api/business/v1/maintenance/plans", MaintenancePermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "createMaintenancePlan"),
        new(typeof(ListMaintenancePlansEndpoint), "GET", "/api/business/v1/maintenance/plans", MaintenancePermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listMaintenancePlans"),
        new(typeof(GenerateDueMaintenanceWorkOrdersEndpoint), "POST", "/api/business/v1/maintenance/plans/generate-due", MaintenancePermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "generateDueMaintenanceWorkOrders"),
        new(typeof(QueryMaintenanceAssetReliabilityEndpoint), "GET", "/api/business/v1/maintenance/assets/{deviceAssetId}/reliability", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "queryMaintenanceAssetReliability"),
        new(typeof(QueryMaintenanceReliabilitySummaryEndpoint), "GET", "/api/business/v1/maintenance/reliability/summary", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "queryMaintenanceReliabilitySummary"),
        new(typeof(RecordMaintenanceInspectionEndpoint), "POST", "/api/business/v1/maintenance/inspections", MaintenancePermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "recordMaintenanceInspection"),
        new(typeof(ListMaintenanceInspectionsEndpoint), "GET", "/api/business/v1/maintenance/inspections", MaintenancePermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceInspections"),
        new(typeof(QueryMaintenanceInspectionMeasurementTrendEndpoint), "GET", "/api/business/v1/maintenance/inspection-measurements/trends", MaintenancePermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "queryMaintenanceInspectionMeasurementTrend"),
        new(typeof(ListMaintenanceSparePartsEndpoint), "GET", "/api/business/v1/maintenance/spare-parts", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceSpareParts"),
        new(typeof(CreateMaintenanceSparePartEndpoint), "POST", "/api/business/v1/maintenance/spare-parts", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "createMaintenanceSparePart"),
        new(typeof(CreateDowntimeReasonEndpoint), "POST", "/api/business/v1/maintenance/downtime-reasons", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "createMaintenanceDowntimeReason"),
        new(typeof(ListDowntimeReasonsEndpoint), "GET", "/api/business/v1/maintenance/downtime-reasons", MaintenancePermissionCodes.WorkOrdersRead, InternalServiceAuthorizationPolicy.Name, "listMaintenanceDowntimeReasons"),
        new(typeof(UpdateDowntimeReasonEndpoint), "PUT", "/api/business/v1/maintenance/downtime-reasons/{reasonCode}", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "updateMaintenanceDowntimeReason"),
        new(typeof(DeleteDowntimeReasonEndpoint), "DELETE", "/api/business/v1/maintenance/downtime-reasons/{reasonCode}", MaintenancePermissionCodes.WorkOrdersManage, InternalServiceAuthorizationPolicy.Name, "deleteMaintenanceDowntimeReason"),
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
