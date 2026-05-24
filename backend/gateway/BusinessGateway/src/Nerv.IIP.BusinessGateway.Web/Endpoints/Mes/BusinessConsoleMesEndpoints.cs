using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

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
