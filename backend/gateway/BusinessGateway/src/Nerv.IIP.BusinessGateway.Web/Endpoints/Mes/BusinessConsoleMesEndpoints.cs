using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness")]
[BusinessGatewayOperationId("getBusinessConsoleMesFoundationReadiness")]
public sealed class GetBusinessConsoleMesFoundationReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesFoundationReadinessRequest, BusinessConsoleMesFoundationReadinessResponse>(
        auth,
        BusinessGatewayPermissions.MesFoundationRead)
{
    private static readonly string[] AreaCodes =
    [
        "master-data",
        "product-engineering",
        "supply",
        "quality",
        "equipment",
        "barcode-numbering",
        "iam-context",
    ];

    protected override string OrganizationId(BusinessConsoleMesFoundationReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesFoundationReadinessRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesFoundationReadinessResponse> ForwardAsync(
        BusinessConsoleMesFoundationReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var areas = new List<BusinessConsoleMesReadinessArea>(AreaCodes.Length);
        foreach (var areaCode in AreaCodes)
        {
            areas.Add(await ReadAreaAsync(areaCode, request, cancellationToken));
        }

        return BuildReadiness(areas);
    }

    private async Task<BusinessConsoleMesReadinessArea> ReadAreaAsync(
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mes.GetFoundationReadinessAreaAsync(
                tokenProvider.BearerToken,
                areaCode,
                request,
                cancellationToken);
        }
        catch (BusinessServiceProxyException)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (HttpRequestException)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (InvalidOperationException)
        {
            return SourceUnavailableArea(areaCode);
        }
    }

    private static BusinessConsoleMesFoundationReadinessResponse BuildReadiness(
        IReadOnlyCollection<BusinessConsoleMesReadinessArea> areas)
    {
        var blockingIssues = areas.SelectMany(area => area.Issues)
            .Where(issue => string.Equals(issue.Severity, "Blocked", StringComparison.Ordinal))
            .ToArray();
        var warningIssues = areas.SelectMany(area => area.Issues)
            .Where(issue => string.Equals(issue.Severity, "Warning", StringComparison.Ordinal))
            .ToArray();
        var status = blockingIssues.Length > 0
            ? "Blocked"
            : warningIssues.Length > 0
                ? "Warning"
                : "Ready";
        return new BusinessConsoleMesFoundationReadinessResponse(status, areas, blockingIssues, warningIssues);
    }

    private static BusinessConsoleMesReadinessArea SourceUnavailableArea(string areaCode) =>
        new(
            areaCode,
            "Blocked",
            [
                new BusinessConsoleMesReadinessIssue(
                    "SOURCE_SERVICE_UNAVAILABLE",
                    "Blocked",
                    "Source service is unavailable or returned invalid readiness data.",
                    areaCode,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "请稍后重试或联系管理员检查来源服务"),
            ]);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/overview")]
[BusinessGatewayOperationId("getBusinessConsoleMesOverview")]
public sealed class GetBusinessConsoleMesOverviewEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesContextRequest, BusinessConsoleMesOverviewResponse>(
        auth,
        BusinessGatewayPermissions.MesOverviewRead)
{
    protected override string OrganizationId(BusinessConsoleMesContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesOverviewResponse> ForwardAsync(
        BusinessConsoleMesContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetOverviewAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders")]
[BusinessGatewayOperationId("listBusinessConsoleMesWorkOrders")]
public sealed class ListBusinessConsoleMesWorkOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesWorkOrderListResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesWorkOrderListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListWorkOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderDetail")]
public sealed class GetBusinessConsoleMesWorkOrderDetailEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesWorkOrderDetailRequest, BusinessConsoleMesWorkOrderDetailResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersRead)
{
    protected override string OrganizationId(BusinessConsoleMesWorkOrderDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesWorkOrderDetailRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesWorkOrderDetailResponse> ForwardAsync(
        BusinessConsoleMesWorkOrderDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetWorkOrderDetailAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/rush")]
[BusinessGatewayOperationId("createBusinessConsoleMesRushWorkOrder")]
public sealed class CreateBusinessConsoleMesRushWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateRushWorkOrderRequest, BusinessConsoleCreateRushWorkOrderResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleCreateRushWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateRushWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateRushWorkOrderResponse> ForwardAsync(
        BusinessConsoleCreateRushWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateRushWorkOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness")]
[BusinessGatewayOperationId("getBusinessConsoleMesMaterialReadiness")]
public sealed class GetBusinessConsoleMesMaterialReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesMaterialReadinessRequest, BusinessConsoleMesMaterialReadinessResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(BusinessConsoleMesMaterialReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesMaterialReadinessRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesMaterialReadinessResponse> ForwardAsync(
        BusinessConsoleMesMaterialReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetMaterialReadinessAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/operation-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesOperationTasks")]
public sealed class ListBusinessConsoleMesOperationTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesOperationTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesOperationTaskListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListOperationTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/wip")]
[BusinessGatewayOperationId("getBusinessConsoleMesWipSummary")]
public sealed class GetBusinessConsoleMesWipSummaryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesWipSummaryResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesWipSummaryResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetWipSummaryAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("listBusinessConsoleMesProductionReports")]
public sealed class ListBusinessConsoleMesProductionReportsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesProductionReportListResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionReportListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListProductionReportsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("recordBusinessConsoleMesProductionReport")]
public sealed class RecordBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordProductionReportRequest, BusinessConsoleRecordProductionReportResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleRecordProductionReportRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordProductionReportRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleRecordProductionReportResponse> ForwardAsync(
        BusinessConsoleRecordProductionReportRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RecordProductionReportAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/finished-goods-receipt-requests")]
[BusinessGatewayOperationId("listBusinessConsoleMesFinishedGoodsReceiptRequests")]
public sealed class ListBusinessConsoleMesFinishedGoodsReceiptRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesReceiptRequestListResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReceiptRequestListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListFinishedGoodsReceiptRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/finished-goods-receipt-requests")]
[BusinessGatewayOperationId("createBusinessConsoleMesFinishedGoodsReceiptRequest")]
public sealed class CreateBusinessConsoleMesFinishedGoodsReceiptRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateReceiptRequest, BusinessConsoleMesCreateReceiptResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesCreateReceiptResponse> ForwardAsync(
        BusinessConsoleMesCreateReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateFinishedGoodsReceiptRequestAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/capacity-impacts")]
[BusinessGatewayOperationId("listBusinessConsoleMesCapacityImpacts")]
public sealed class ListBusinessConsoleMesCapacityImpactsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesCapacityImpactListResponse>(
        auth,
        BusinessGatewayPermissions.MesCapacityRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesCapacityImpactListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListCapacityImpactsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/schedules/run")]
[BusinessGatewayOperationId("runBusinessConsoleMesSchedule")]
public sealed class RunBusinessConsoleMesScheduleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRunScheduleRequest, BusinessConsoleMesScheduleResult>(
        auth,
        BusinessGatewayPermissions.MesSchedulesManage)
{
    protected override string OrganizationId(BusinessConsoleRunScheduleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRunScheduleRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesScheduleResult> ForwardAsync(
        BusinessConsoleRunScheduleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RunScheduleAsync(tokenProvider.BearerToken, request, cancellationToken);
}
