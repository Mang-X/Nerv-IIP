using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

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

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 CreateRushWorkOrderCommandHandler 走 CodeAllocator，落 mes.code_idempotency_keys.idempotency_key(150)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateRushWorkOrderRequestValidator
    : Validator<BusinessConsoleCreateRushWorkOrderRequest>
{
    public BusinessConsoleCreateRushWorkOrderRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
