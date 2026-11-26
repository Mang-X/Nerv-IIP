using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("listBusinessConsoleMesProductionReports")]
public sealed class ListBusinessConsoleMesProductionReportsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessIamAccountDirectoryClient iamAccounts,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListWithoutStatusRequest, BusinessConsoleMesProductionReportListResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesListWithoutStatusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListWithoutStatusRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesProductionReportListResponse> ForwardAsync(
        BusinessConsoleMesListWithoutStatusRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var response = await mes.ListProductionReportsAsync(tokenProvider.BearerToken, request, cancellationToken);
        var names = await MesOperatorDisplayNames.ResolveAsync(iamAccounts, tokenProvider.BearerToken,
            request.OrganizationId, request.EnvironmentId, response.Items.Select(report => report.ReportedBy), cancellationToken);
        return response with
        {
            Items = response.Items.Select(report => report with
            {
                ReportedByDisplayName = report.ReportedBy is { } userId ? names.GetValueOrDefault(userId) : null,
            }).ToArray(),
        };
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-statistics")]
[BusinessGatewayOperationId("queryBusinessConsoleMesProductionStatistics")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status502BadGateway)]
public sealed class QueryBusinessConsoleMesProductionStatisticsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionStatisticsRequest, BusinessConsoleMesProductionStatisticsResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionStatisticsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionStatisticsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionStatisticsResponse> ForwardAsync(
        BusinessConsoleMesProductionStatisticsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.QueryProductionStatisticsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleMesProductionStatisticsRequestValidator
    : Validator<BusinessConsoleMesProductionStatisticsRequest>
{
    public BusinessConsoleMesProductionStatisticsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.ShiftCode).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.SkuId).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-reports/{reportNo}")]
[BusinessGatewayOperationId("getBusinessConsoleMesProductionReport")]
public sealed class GetBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessIamAccountDirectoryClient iamAccounts,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionReportDetailRequest, BusinessConsoleMesProductionReportDetailResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionReportDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionReportDetailRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesProductionReportDetailResponse> ForwardAsync(
        BusinessConsoleMesProductionReportDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var response = await mes.GetProductionReportAsync(
            tokenProvider.BearerToken,
            request.ReportNo,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        var names = await MesOperatorDisplayNames.ResolveAsync(iamAccounts, tokenProvider.BearerToken,
            request.OrganizationId, request.EnvironmentId, [response.Report.ReportedBy], cancellationToken);
        return response with
        {
            Report = response.Report with
            {
                ReportedByDisplayName = response.Report.ReportedBy is { } userId ? names.GetValueOrDefault(userId) : null,
            },
        };
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("recordBusinessConsoleMesProductionReport")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class RecordBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesProductionReportCoordinator coordinator,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordProductionReportRequest, BusinessConsoleRecordProductionReportResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingWrite)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleRecordProductionReportRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordProductionReportRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleRecordProductionReportResponse> ForwardAsync(
        BusinessConsoleRecordProductionReportRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var operationTask = await workScopeAuthorizer.EnsureOperationTaskAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesReportingWrite,
            request.ScopeKind,
            request.ScopeId,
            request.OperationTaskId,
            cancellationToken);
        if (!string.Equals(operationTask.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromLocalRejection(
                System.Net.HttpStatusCode.Forbidden,
                PrincipalWorkScopeStableWireCodes.NotAuthorized);
        }
        return await coordinator.RecordAsync(
            tokenProvider.BearerToken,
            request,
            RequireAuthorizedPrincipalActor().ActorRef,
            cancellationToken);
    }
}

public sealed class BusinessConsoleRecordProductionReportRequestValidator
    : Validator<BusinessConsoleRecordProductionReportRequest>
{
    public BusinessConsoleRecordProductionReportRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
    }
}
