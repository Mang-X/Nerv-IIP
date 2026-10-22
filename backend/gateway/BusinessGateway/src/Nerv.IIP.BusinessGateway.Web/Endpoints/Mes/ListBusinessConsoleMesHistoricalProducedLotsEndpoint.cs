using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/work-orders/{workOrderId}/produced-lots")]
[BusinessGatewayOperationId("listBusinessConsoleMesHistoricalProducedLots")]
public sealed class ListBusinessConsoleMesHistoricalProducedLotsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByWorkOrderRequest, BusinessConsoleMesHistoricalProducedLotListResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesHistoricalProducedLotListResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAcrossAuthorizedScopesAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesTraceabilityRead,
            request.WorkOrderId,
            cancellationToken);
        return await mes.ListHistoricalProducedLotsAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
    }
}
