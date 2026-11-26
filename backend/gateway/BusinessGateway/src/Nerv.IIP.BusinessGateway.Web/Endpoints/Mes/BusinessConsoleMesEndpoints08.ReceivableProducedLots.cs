using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}/produced-lots")]
[BusinessGatewayOperationId("listBusinessConsoleMesReceivableProducedLots")]
public sealed class ListBusinessConsoleMesReceivableProducedLotsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReceivableProducedLotsRequest, BusinessConsoleMesReceivableProducedLotListResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleMesReceivableProducedLotsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReceivableProducedLotsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReceivableProducedLotListResponse> ForwardAsync(
        BusinessConsoleMesReceivableProducedLotsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListReceivableProducedLotsAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}
