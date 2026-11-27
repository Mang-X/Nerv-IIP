using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/quality-holds/{sourceDocumentId}/force-release")]
[BusinessGatewayOperationId("forceReleaseBusinessConsoleMesQualityHold")]
public sealed class ForceReleaseBusinessConsoleMesQualityHoldEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesForceReleaseQualityHoldRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityWrite)
{
    protected override string OrganizationId(BusinessConsoleMesForceReleaseQualityHoldRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesForceReleaseQualityHoldRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var audit = RequireIdempotentAuditContext(request);
        return mes.ForceReleaseQualityHoldAsync(
            tokenProvider.BearerToken,
            request.SourceDocumentId,
            request,
            audit.Actor,
            audit.CorrelationId,
            cancellationToken);
    }
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 ForceReleaseQualityHoldCommandValidator(512) 与落库列 mes.quality_hold_transitions.idempotency_key(512)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesForceReleaseQualityHoldRequestValidator
    : Validator<BusinessConsoleMesForceReleaseQualityHoldRequest>
{
    public BusinessConsoleMesForceReleaseQualityHoldRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(512);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/quality-holds/{sourceDocumentId}/timeline")]
[BusinessGatewayOperationId("getBusinessConsoleMesQualityHoldTimeline")]
public sealed class GetBusinessConsoleMesQualityHoldTimelineEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesQualityHoldTimelineRequest, BusinessConsoleMesQualityHoldTimelineResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityRead)
{
    protected override string OrganizationId(BusinessConsoleMesQualityHoldTimelineRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesQualityHoldTimelineRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleMesQualityHoldTimelineResponse> ForwardAsync(
        BusinessConsoleMesQualityHoldTimelineRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetQualityHoldTimelineAsync(tokenProvider.BearerToken, request.SourceDocumentId, request, cancellationToken);
}
