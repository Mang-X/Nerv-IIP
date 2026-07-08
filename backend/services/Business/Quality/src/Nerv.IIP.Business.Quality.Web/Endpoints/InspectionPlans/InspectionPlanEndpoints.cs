using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;

public sealed record CreateInspectionPlanRequest(
    string OrganizationId,
    string EnvironmentId,
    string PlanCode,
    string Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? DocumentType,
    IReadOnlyCollection<InspectionPlanCharacteristicInput>? Characteristics);

public sealed record CreateInspectionPlanResponse(InspectionPlanId InspectionPlanId);

public sealed record ActivateInspectionPlanRequest(InspectionPlanId InspectionPlanId);

public sealed record ListInspectionPlansRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? Status,
    string? Keyword,
    int Skip = 0,
    int Take = 100);

public sealed record ListInspectionPlansEndpointResponse(IReadOnlyCollection<InspectionPlanResponse> Items, int Total);

public sealed class CreateInspectionPlanEndpoint(ISender sender)
    : QualityEndpoint<CreateInspectionPlanRequest, ResponseData<CreateInspectionPlanResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<CreateInspectionPlanEndpoint>());
    }

    public override async Task HandleAsync(CreateInspectionPlanRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateInspectionPlanCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.PlanCode,
            req.Category,
            req.SkuCode,
            req.PartnerId,
            req.WorkCenterId,
            req.DeviceAssetId,
            req.DocumentType,
            req.Characteristics ?? []), ct);
        await Send.OkAsync(new CreateInspectionPlanResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ActivateInspectionPlanEndpoint(ISender sender)
    : QualityEndpoint<ActivateInspectionPlanRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<ActivateInspectionPlanEndpoint>());
    }

    public override async Task HandleAsync(ActivateInspectionPlanRequest req, CancellationToken ct)
    {
        await sender.Send(new ActivateInspectionPlanCommand(req.InspectionPlanId), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListInspectionPlansEndpoint(ISender sender)
    : QualityEndpoint<ListInspectionPlansRequest, ResponseData<ListInspectionPlansEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<ListInspectionPlansEndpoint>());
    }

    public override async Task HandleAsync(ListInspectionPlansRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListInspectionPlansQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Category,
            req.SkuCode,
            req.PartnerId,
            req.WorkCenterId,
            req.Status,
            req.Keyword,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(new ListInspectionPlansEndpointResponse(response.Items, response.Total).AsResponseData(), cancellation: ct);
    }
}

public sealed record QualityInspectionEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class QualityInspectionEndpointContracts
{
    public static readonly IReadOnlyCollection<QualityInspectionEndpointContract> All =
    [
        new(typeof(CreateInspectionPlanEndpoint), "POST", "/api/business/v1/quality/inspection-plans", BusinessPermissionCodes.QualityInspectionPlansManage, "createBusinessQualityInspectionPlan"),
        new(typeof(ActivateInspectionPlanEndpoint), "POST", "/api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate", BusinessPermissionCodes.QualityInspectionPlansManage, "activateBusinessQualityInspectionPlan"),
        new(typeof(ListInspectionPlansEndpoint), "GET", "/api/business/v1/quality/inspection-plans", BusinessPermissionCodes.QualityInspectionRecordsRead, "listBusinessQualityInspectionPlans"),
        new(typeof(InspectionRecords.CreateInspectionRecordEndpoint), "POST", "/api/business/v1/quality/inspection-records", BusinessPermissionCodes.QualityInspectionRecordsCreate, "createBusinessQualityInspectionRecord"),
        new(typeof(InspectionRecords.OpenNcrFromInspectionEndpoint), "POST", "/api/business/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr", BusinessPermissionCodes.QualityNcrManage, "openBusinessQualityNcrFromInspection"),
        new(typeof(InspectionRecords.ListInspectionRecordsEndpoint), "GET", "/api/business/v1/quality/inspection-records", BusinessPermissionCodes.QualityInspectionRecordsRead, "listBusinessQualityInspectionRecords"),
        new(typeof(InspectionTasks.ListInspectionTasksEndpoint), "GET", "/api/business/v1/quality/inspection-tasks", BusinessPermissionCodes.QualityInspectionRecordsRead, "listBusinessQualityInspectionTasks"),
        new(typeof(InspectionTasks.CreateInspectionRecordFromTaskEndpoint), "POST", "/api/business/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record", BusinessPermissionCodes.QualityInspectionRecordsCreate, "createBusinessQualityInspectionRecordFromTask"),
        new(typeof(Nerv.IIP.Business.Quality.Web.Endpoints.Spc.QuerySpcControlChartEndpoint), "GET", "/api/business/v1/quality/spc/control-chart", BusinessPermissionCodes.QualityInspectionRecordsRead, "queryBusinessQualitySpcControlChart"),
        new(typeof(Nerv.IIP.Business.Quality.Web.Endpoints.Spc.QueryProcessCapabilityEndpoint), "GET", "/api/business/v1/quality/spc/process-capability", BusinessPermissionCodes.QualityInspectionRecordsRead, "queryBusinessQualityProcessCapability"),
        new(typeof(Nerv.IIP.Business.Quality.Web.Endpoints.Spc.EvaluateSpcControlChartEndpoint), "POST", "/api/business/v1/quality/spc/control-chart/evaluate", BusinessPermissionCodes.QualitySpcManage, "evaluateBusinessQualitySpcControlChart"),
        new(typeof(Nerv.IIP.Business.Quality.Web.Endpoints.Spc.LockSpcControlChartEndpoint), "POST", "/api/business/v1/quality/spc/control-chart/lock", BusinessPermissionCodes.QualitySpcManage, "lockBusinessQualitySpcControlChart"),
    ];

    public static QualityEndpointContract Get<TEndpoint>()
    {
        var contract = All.Single(x => x.EndpointType == typeof(TEndpoint));
        return new QualityEndpointContract(contract.EndpointType, contract.HttpMethod, contract.Route, contract.PermissionCode, contract.OperationId);
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out QualityInspectionEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
