using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-reports/{reportNo}/reverse")]
[BusinessGatewayOperationId("reverseBusinessConsoleMesProductionReport")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ReverseBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReverseProductionReportRequest, BusinessConsoleMesReverseProductionReportResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleMesReverseProductionReportRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReverseProductionReportRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReverseProductionReportResponse> ForwardAsync(
        BusinessConsoleMesReverseProductionReportRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ReverseProductionReportAsync(
            tokenProvider.BearerToken,
            request.ReportNo,
            request,
            RequireAuthorizedPrincipalActor().ActorRef,
            cancellationToken);
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 ReverseProductionReportCommandHandler 走 CodeAllocator，落 mes.code_idempotency_keys.idempotency_key(150)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesReverseProductionReportRequestValidator
    : Validator<BusinessConsoleMesReverseProductionReportRequest>
{
    public BusinessConsoleMesReverseProductionReportRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
