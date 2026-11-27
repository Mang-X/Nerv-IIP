using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/telemetry-production-report-candidates")]
[BusinessGatewayOperationId("listBusinessConsoleMesTelemetryProductionReportCandidates")]
public sealed class ListBusinessConsoleMesTelemetryCandidatesEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidateListRequest, BusinessConsoleMesTelemetryCandidateListResponse>(auth, BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidateListRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidateListRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleMesTelemetryCandidateListResponse> ForwardAsync(BusinessConsoleMesTelemetryCandidateListRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.ListTelemetryCandidatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/telemetry-production-report-candidates/{candidateId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesTelemetryProductionReportCandidate")]
public sealed class GetBusinessConsoleMesTelemetryCandidateEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidateDetailRequest, BusinessConsoleMesTelemetryCandidateRow>(auth, BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidateDetailRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidateDetailRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleMesTelemetryCandidateRow> ForwardAsync(BusinessConsoleMesTelemetryCandidateDetailRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.GetTelemetryCandidateAsync(tokenProvider.BearerToken, request.CandidateId, request.OrganizationId, request.EnvironmentId, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/telemetry-production-report-candidates/{candidateId}/promote")]
[BusinessGatewayOperationId("promoteBusinessConsoleMesTelemetryProductionReportCandidate")]
public sealed class PromoteBusinessConsoleMesTelemetryCandidateEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidatePromoteRequest, BusinessConsoleRecordProductionReportResponse>(auth, BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidatePromoteRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidatePromoteRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleRecordProductionReportResponse> ForwardAsync(BusinessConsoleMesTelemetryCandidatePromoteRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.PromoteTelemetryCandidateAsync(tokenProvider.BearerToken, request.CandidateId, request, RequireAuthorizedPrincipalActor().ActorRef, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/telemetry-production-report-candidates/{candidateId}/dismiss")]
[BusinessGatewayOperationId("dismissBusinessConsoleMesTelemetryProductionReportCandidate")]
public sealed class DismissBusinessConsoleMesTelemetryCandidateEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidateDismissRequest, BusinessConsoleAcceptedResponse>(auth, BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidateDismissRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidateDismissRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(BusinessConsoleMesTelemetryCandidateDismissRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.DismissTelemetryCandidateAsync(tokenProvider.BearerToken, request.CandidateId, request, RequireAuthorizedPrincipalActor().ActorRef, cancellationToken);
}
