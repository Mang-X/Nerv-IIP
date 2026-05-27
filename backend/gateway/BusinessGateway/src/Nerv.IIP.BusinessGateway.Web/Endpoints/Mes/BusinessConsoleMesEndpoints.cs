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

public abstract class GetBusinessConsoleMesReadinessAreaEndpoint(
    string areaCode,
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesFoundationReadinessRequest, BusinessConsoleMesReadinessArea>(
        auth,
        BusinessGatewayPermissions.MesFoundationRead)
{
    protected override string OrganizationId(BusinessConsoleMesFoundationReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesFoundationReadinessRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReadinessArea> ForwardAsync(
        BusinessConsoleMesFoundationReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetFoundationReadinessAreaAsync(tokenProvider.BearerToken, areaCode, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/master-data")]
[BusinessGatewayOperationId("getBusinessConsoleMesMasterDataReadiness")]
public sealed class GetBusinessConsoleMesMasterDataReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("master-data", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/product-engineering")]
[BusinessGatewayOperationId("getBusinessConsoleMesProductEngineeringReadiness")]
public sealed class GetBusinessConsoleMesProductEngineeringReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("product-engineering", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/supply")]
[BusinessGatewayOperationId("getBusinessConsoleMesSupplyReadiness")]
public sealed class GetBusinessConsoleMesSupplyReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("supply", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/quality")]
[BusinessGatewayOperationId("getBusinessConsoleMesQualityReadiness")]
public sealed class GetBusinessConsoleMesQualityReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("quality", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/equipment")]
[BusinessGatewayOperationId("getBusinessConsoleMesEquipmentReadiness")]
public sealed class GetBusinessConsoleMesEquipmentReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("equipment", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/barcode-numbering")]
[BusinessGatewayOperationId("getBusinessConsoleMesBarcodeNumberingReadiness")]
public sealed class GetBusinessConsoleMesBarcodeNumberingReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("barcode-numbering", auth, mes, tokenProvider);

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
[HttpGet("/api/business-console/v1/mes/production-plans")]
[BusinessGatewayOperationId("listBusinessConsoleMesProductionPlans")]
public sealed class ListBusinessConsoleMesProductionPlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesProductionPlanListResponse>(
        auth,
        BusinessGatewayPermissions.MesPlansRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionPlanListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListProductionPlansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-plans/{productionPlanId}/readiness")]
[BusinessGatewayOperationId("getBusinessConsoleMesProductionPlanReadiness")]
public sealed class GetBusinessConsoleMesProductionPlanReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionPlanReadinessRequest, BusinessConsoleMesFoundationReadinessResponse>(
        auth,
        BusinessGatewayPermissions.MesPlansRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionPlanReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionPlanReadinessRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesFoundationReadinessResponse> ForwardAsync(
        BusinessConsoleMesProductionPlanReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetProductionPlanReadinessAsync(
            tokenProvider.BearerToken,
            request.ProductionPlanId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders")]
[BusinessGatewayOperationId("convertBusinessConsoleMesPlanToWorkOrder")]
public sealed class ConvertBusinessConsoleMesPlanToWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesConvertPlanToWorkOrderRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleMesConvertPlanToWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesConvertPlanToWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConvertPlanToWorkOrderAsync(tokenProvider.BearerToken, request.ProductionPlanId, request, cancellationToken);
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
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleMesWorkOrder")]
public sealed class ReleaseBusinessConsoleMesWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReleaseWorkOrderRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleMesReleaseWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReleaseWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesReleaseWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ReleaseWorkOrderAsync(tokenProvider.BearerToken, request.WorkOrderId, request, cancellationToken);
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
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests")]
[BusinessGatewayOperationId("createBusinessConsoleMesMaterialIssueRequest")]
public sealed class CreateBusinessConsoleMesMaterialIssueRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateMaterialIssueRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateMaterialIssueRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateMaterialIssueRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCreateMaterialIssueRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateMaterialIssueRequestAsync(tokenProvider.BearerToken, request.WorkOrderId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/material-issue-requests")]
[BusinessGatewayOperationId("listBusinessConsoleMesMaterialIssueRequests")]
public sealed class ListBusinessConsoleMesMaterialIssueRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesMaterialIssueRequestListResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesMaterialIssueRequestListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListMaterialIssueRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts")]
[BusinessGatewayOperationId("confirmBusinessConsoleMesLineSideMaterialReceipt")]
public sealed class ConfirmBusinessConsoleMesLineSideMaterialReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesConfirmLineSideReceiptRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesConfirmLineSideReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesConfirmLineSideReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConfirmLineSideMaterialReceiptAsync(tokenProvider.BearerToken, request.RequestId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/dispatch-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesDispatchTasks")]
public sealed class ListBusinessConsoleMesDispatchTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesDispatchTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesDispatchRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesDispatchTaskListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListDispatchTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign")]
[BusinessGatewayOperationId("assignBusinessConsoleMesDispatchTask")]
public sealed class AssignBusinessConsoleMesDispatchTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAssignDispatchTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDispatchManage)
{
    protected override string OrganizationId(BusinessConsoleMesAssignDispatchTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAssignDispatchTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAssignDispatchTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.AssignDispatchTaskAsync(tokenProvider.BearerToken, request.OperationTaskId, request, cancellationToken);
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

public abstract class BusinessConsoleMesOperationTaskActionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesOperationTaskActionRequest, BusinessConsoleMesOperationTaskActionResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsManage)
{
    protected override string OrganizationId(BusinessConsoleMesOperationTaskActionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesOperationTaskActionRequest request) => request.EnvironmentId;

    protected abstract Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardAsync(
        BusinessConsoleMesOperationTaskActionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        ForwardOperationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleMesOperationTask")]
public sealed class StartBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.StartOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause")]
[BusinessGatewayOperationId("pauseBusinessConsoleMesOperationTask")]
public sealed class PauseBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.PauseOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume")]
[BusinessGatewayOperationId("resumeBusinessConsoleMesOperationTask")]
public sealed class ResumeBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.ResumeOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleMesOperationTask")]
public sealed class CompleteBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.CompleteOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
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
[HttpPost("/api/business-console/v1/mes/defects")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDefect")]
public sealed class RecordBusinessConsoleMesDefectEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDefectRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityWrite)
{
    protected override string OrganizationId(BusinessConsoleMesRecordDefectRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDefectRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDefectRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RecordDefectAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/related-quality-items")]
[BusinessGatewayOperationId("listBusinessConsoleMesRelatedQualityItems")]
public sealed class ListBusinessConsoleMesRelatedQualityItemsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesRelatedQualityItemListResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesRelatedQualityItemListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListRelatedQualityItemsAsync(tokenProvider.BearerToken, request, cancellationToken);
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
[HttpGet("/api/business-console/v1/mes/downtime-events")]
[BusinessGatewayOperationId("listBusinessConsoleMesDowntimeEvents")]
public sealed class ListBusinessConsoleMesDowntimeEventsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesDowntimeEventListResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesDowntimeEventListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListDowntimeEventsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/downtime-events")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDowntimeEvent")]
public sealed class RecordBusinessConsoleMesDowntimeEventEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDowntimeEventRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeManage)
{
    protected override string OrganizationId(BusinessConsoleMesRecordDowntimeEventRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDowntimeEventRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDowntimeEventRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RecordDowntimeEventAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/downtime-events/{downtimeEventId}/recover")]
[BusinessGatewayOperationId("confirmBusinessConsoleMesDowntimeRecovery")]
public sealed class ConfirmBusinessConsoleMesDowntimeRecoveryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecoverDowntimeEventRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeManage)
{
    protected override string OrganizationId(BusinessConsoleMesRecoverDowntimeEventRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecoverDowntimeEventRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConfirmDowntimeRecoveryAsync(tokenProvider.BearerToken, request.DowntimeEventId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/shift-handovers")]
[BusinessGatewayOperationId("listBusinessConsoleMesShiftHandovers")]
public sealed class ListBusinessConsoleMesShiftHandoversEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesShiftHandoverListResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesShiftHandoverListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListShiftHandoversAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/shift-handovers")]
[BusinessGatewayOperationId("createBusinessConsoleMesShiftHandover")]
public sealed class CreateBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateShiftHandoverRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateShiftHandoverRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateShiftHandoverRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCreateShiftHandoverRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateShiftHandoverAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/shift-handovers/{handoverId}/accept")]
[BusinessGatewayOperationId("acceptBusinessConsoleMesShiftHandover")]
public sealed class AcceptBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAcceptShiftHandoverRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleMesAcceptShiftHandoverRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAcceptShiftHandoverRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.AcceptShiftHandoverAsync(tokenProvider.BearerToken, request.HandoverId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderTraceability")]
public sealed class GetBusinessConsoleMesWorkOrderTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByWorkOrderRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetWorkOrderTraceabilityAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/batches/{batchOrSerial}")]
[BusinessGatewayOperationId("getBusinessConsoleMesBatchTraceability")]
public sealed class GetBusinessConsoleMesBatchTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByBatchRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByBatchRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByBatchRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByBatchRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetBatchTraceabilityAsync(
            tokenProvider.BearerToken,
            request.BatchOrSerial,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/material-lots/{materialLotId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesMaterialLotTraceability")]
public sealed class GetBusinessConsoleMesMaterialLotTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByMaterialLotRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByMaterialLotRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByMaterialLotRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByMaterialLotRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetMaterialLotTraceabilityAsync(
            tokenProvider.BearerToken,
            request.MaterialLotId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
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
