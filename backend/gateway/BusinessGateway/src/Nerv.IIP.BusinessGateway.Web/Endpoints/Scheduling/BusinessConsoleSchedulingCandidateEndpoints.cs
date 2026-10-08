using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/workbench/candidates/preview")]
[BusinessGatewayOperationId("previewBusinessConsoleSchedulingCandidates")]
public sealed class PreviewBusinessConsoleSchedulingCandidatesEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<SchedulingCandidatePreviewRequestContract, SchedulingCandidateSetContract>(auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(SchedulingCandidatePreviewRequestContract request) => request.OrganizationId;
    protected override string EnvironmentId(SchedulingCandidatePreviewRequestContract request) => request.EnvironmentId;
    protected override Task<SchedulingCandidateSetContract> ForwardAsync(SchedulingCandidatePreviewRequestContract request, string bearerToken, CancellationToken ct) =>
        scheduling.PreviewCandidatesAsync(tokenProvider.BearerToken, request, ct);
}
[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/workbench/candidates/select")]
[BusinessGatewayOperationId("selectBusinessConsoleSchedulingCandidate")]
public sealed class SelectBusinessConsoleSchedulingCandidateEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<SchedulingCandidateSelectRequestContract, SchedulingCandidateSelectionContract>(auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(SchedulingCandidateSelectRequestContract request) => request.OrganizationId;
    protected override string EnvironmentId(SchedulingCandidateSelectRequestContract request) => request.EnvironmentId;
    protected override Task<SchedulingCandidateSelectionContract> ForwardAsync(SchedulingCandidateSelectRequestContract request, string bearerToken, CancellationToken ct) =>
        scheduling.SelectCandidateAsync(tokenProvider.BearerToken, request, RequireAuthorizedPrincipalId(), ct);
}
