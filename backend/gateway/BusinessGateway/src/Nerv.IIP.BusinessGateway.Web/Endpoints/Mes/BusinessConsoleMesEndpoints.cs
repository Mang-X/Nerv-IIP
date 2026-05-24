using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders")]
[BusinessGatewayOperationId("listBusinessConsoleMesWorkOrders")]
public sealed class ListBusinessConsoleMesWorkOrdersEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.MesWorkOrdersRead);

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/rush")]
[BusinessGatewayOperationId("createBusinessConsoleMesRushWorkOrder")]
public sealed class CreateBusinessConsoleMesRushWorkOrderEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.MesWorkOrdersManage);

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/schedules/run")]
[BusinessGatewayOperationId("runBusinessConsoleMesSchedule")]
public sealed class RunBusinessConsoleMesScheduleEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.MesSchedulesManage);

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("recordBusinessConsoleMesProductionReport")]
public sealed class RecordBusinessConsoleMesProductionReportEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessStubEndpoint(auth, BusinessGatewayPermissions.MesReportingWrite);
