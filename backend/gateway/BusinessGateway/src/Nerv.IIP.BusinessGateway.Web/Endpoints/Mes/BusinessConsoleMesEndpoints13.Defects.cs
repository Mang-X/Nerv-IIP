using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/defects")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDefect")]
public sealed class RecordBusinessConsoleMesDefectEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider,
    TimeProvider timeProvider)
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
        mes.RecordDefectAsync(
            tokenProvider.BearerToken,
            new BusinessMesRecordDefectRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.DefectCode,
                request.DefectQuantity,
                timeProvider.GetUtcNow(),
                request.IdempotencyKey),
            cancellationToken);
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 RecordDefectRequestValidator(150) 与 CodeAllocator 落库列 mes.code_idempotency_keys.idempotency_key(150)，取最小。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesRecordDefectRequestValidator
    : Validator<BusinessConsoleMesRecordDefectRequest>
{
    public BusinessConsoleMesRecordDefectRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v2/mes/defects")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDefectV2")]
public sealed class RecordBusinessConsoleMesDefectV2Endpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDefectV2Request, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityWrite)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesRecordDefectV2Request request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDefectV2Request request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDefectV2Request request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesQualityWrite,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await mes.RecordDefectAsync(
            tokenProvider.BearerToken,
            new BusinessMesRecordDefectRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.DefectCode,
                request.Quantity,
                request.RecordedAtUtc,
                request.IdempotencyKey),
            cancellationToken);
    }
}

public sealed class BusinessConsoleMesRecordDefectV2RequestValidator
    : Validator<BusinessConsoleMesRecordDefectV2Request>
{
    public BusinessConsoleMesRecordDefectV2RequestValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OperationTaskId).MaximumLength(200);
        RuleFor(x => x.DefectCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).NotEmpty().GreaterThan(0);
        RuleFor(x => x.RecordedAtUtc).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
    }
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
