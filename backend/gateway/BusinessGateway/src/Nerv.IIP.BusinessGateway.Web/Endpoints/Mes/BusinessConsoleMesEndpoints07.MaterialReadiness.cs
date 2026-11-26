using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

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
