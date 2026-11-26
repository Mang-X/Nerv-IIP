using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-plans")]
[BusinessGatewayOperationId("listBusinessConsoleMesProductionPlans")]
public sealed class ListBusinessConsoleMesProductionPlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionPlanListRequest, BusinessConsoleMesProductionPlanListResponse>(
        auth,
        BusinessGatewayPermissions.MesPlansRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionPlanListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionPlanListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionPlanListResponse> ForwardAsync(
        BusinessConsoleMesProductionPlanListRequest request,
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

    protected override async Task<BusinessConsoleMesFoundationReadinessResponse> ForwardAsync(
        BusinessConsoleMesProductionPlanReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mes.GetProductionPlanReadinessAsync(
                tokenProvider.BearerToken,
                request.ProductionPlanId,
                new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
                cancellationToken);
        }
        catch (BusinessServiceProxyException)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
        catch (HttpRequestException)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
        catch (InvalidOperationException)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
    }

    private static BusinessConsoleMesFoundationReadinessResponse SourceUnavailableReadiness(string productionPlanId)
    {
        var issue = new BusinessConsoleMesReadinessIssue(
            "SOURCE_SERVICE_UNAVAILABLE",
            "Blocked",
            "Business MES readiness service is unavailable or returned invalid readiness data.",
            "BusinessMes",
            "ProductionPlan",
            productionPlanId,
            productionPlanId,
            null,
            null,
            null,
            "请稍后重试或联系管理员检查 MES 就绪服务");
        var area = new BusinessConsoleMesReadinessArea("mes-readiness", "Blocked", [issue]);
        return new BusinessConsoleMesFoundationReadinessResponse("Blocked", [area], [issue], []);
    }
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

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 ConvertPlanToWorkOrderCommandHandler 把原始键交给 CodeAllocator，落 mes.code_idempotency_keys.idempotency_key(150)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesConvertPlanToWorkOrderRequestValidator
    : Validator<BusinessConsoleMesConvertPlanToWorkOrderRequest>
{
    public BusinessConsoleMesConvertPlanToWorkOrderRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
