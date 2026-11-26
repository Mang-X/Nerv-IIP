using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests")]
[BusinessGatewayOperationId("createBusinessConsoleMesMaterialIssueRequest")]
public sealed class CreateBusinessConsoleMesMaterialIssueRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateMaterialIssueRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateMaterialIssueRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateMaterialIssueRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCreateMaterialIssueRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateMaterialIssueRequestAsync(tokenProvider.BearerToken, request.WorkOrderId, request, cancellationToken);
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 CreateMaterialIssueRequestCommandHandler 走 CodeAllocator，落 mes.code_idempotency_keys.idempotency_key(150)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesCreateMaterialIssueRequestValidator
    : Validator<BusinessConsoleMesCreateMaterialIssueRequest>
{
    public BusinessConsoleMesCreateMaterialIssueRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/material-issue-requests")]
[BusinessGatewayOperationId("listBusinessConsoleMesMaterialIssueRequests")]
public sealed class ListBusinessConsoleMesMaterialIssueRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesMaterialIssueRequestListRequest, BusinessConsoleMesMaterialIssueRequestListResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(BusinessConsoleMesMaterialIssueRequestListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesMaterialIssueRequestListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesMaterialIssueRequestListResponse> ForwardAsync(
        BusinessConsoleMesMaterialIssueRequestListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListMaterialIssueRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/material-issue-requests/{requestId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesMaterialIssueRequest")]
public sealed class GetBusinessConsoleMesMaterialIssueRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesMaterialIssueRequestDetailRequest, BusinessConsoleMesMaterialIssueRequestRow>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(BusinessConsoleMesMaterialIssueRequestDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesMaterialIssueRequestDetailRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesMaterialIssueRequestRow> ForwardAsync(
        BusinessConsoleMesMaterialIssueRequestDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetMaterialIssueRequestAsync(tokenProvider.BearerToken, request.RequestId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/material-scan-prevalidation")]
[BusinessGatewayOperationId("prevalidateBusinessConsoleMesMaterialScan")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status400BadRequest)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status502BadGateway)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status503ServiceUnavailable)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status504GatewayTimeout)]
public sealed class PrevalidateBusinessConsoleMesMaterialScanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesMaterialPrevalidationClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<MesMaterialScanPrevalidationRequest, MesMaterialScanPrevalidationResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(MesMaterialScanPrevalidationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(MesMaterialScanPrevalidationRequest request) => request.EnvironmentId;

    protected override Task<MesMaterialScanPrevalidationResponse> ForwardAsync(
        MesMaterialScanPrevalidationRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.Response.Headers["X-Correlation-Id"].Single()
            ?? throw new InvalidOperationException("Correlation middleware did not establish X-Correlation-Id.");
        return mes.PrevalidateAsync(tokenProvider.BearerToken, correlationId, request, cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/context-scan-prevalidation")]
[BusinessGatewayOperationId("prevalidateBusinessConsoleMesContextScan")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status400BadRequest)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status502BadGateway)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status503ServiceUnavailable)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status504GatewayTimeout)]
public sealed class PrevalidateBusinessConsoleMesContextScanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesContextPrevalidationClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<MesContextScanPrevalidationRequest, MesContextScanPrevalidationResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(MesContextScanPrevalidationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(MesContextScanPrevalidationRequest request) => request.EnvironmentId;

    protected override Task<MesContextScanPrevalidationResponse> ForwardAsync(
        MesContextScanPrevalidationRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.Response.Headers["X-Correlation-Id"].Single()
            ?? throw new InvalidOperationException("Correlation middleware did not establish X-Correlation-Id.");
        return mes.PrevalidateAsync(tokenProvider.BearerToken, correlationId, request, cancellationToken);
    }
}

public sealed class BusinessConsoleMesConfirmLineSideReceiptRequestValidator
    : Validator<BusinessConsoleMesConfirmLineSideReceiptRequest>
{
    public BusinessConsoleMesConfirmLineSideReceiptRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150)
            .When(x => x.IdempotencyKey is not null);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts")]
[BusinessGatewayOperationId("confirmBusinessConsoleMesLineSideMaterialReceipt")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ConfirmBusinessConsoleMesLineSideMaterialReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesConfirmLineSideReceiptRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesConfirmLineSideReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesConfirmLineSideReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConfirmLineSideMaterialReceiptAsync(tokenProvider.BearerToken, request.RequestId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-returns")]
[BusinessGatewayOperationId("returnBusinessConsoleMesLineSideMaterial")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ReturnBusinessConsoleMesLineSideMaterialEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReturnLineSideMaterialRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesReturnLineSideMaterialRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReturnLineSideMaterialRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesReturnLineSideMaterialRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ReturnLineSideMaterialAsync(tokenProvider.BearerToken, request.RequestId, request, cancellationToken);
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 ReturnLineSideMaterialCommandValidator(150)；承载列是无界 text，上界只由该校验器承担。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesReturnLineSideMaterialRequestValidator
    : Validator<BusinessConsoleMesReturnLineSideMaterialRequest>
{
    public BusinessConsoleMesReturnLineSideMaterialRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
