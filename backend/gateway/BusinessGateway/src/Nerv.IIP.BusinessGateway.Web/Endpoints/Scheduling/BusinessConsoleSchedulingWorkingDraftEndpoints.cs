using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

[Tags("Business Console Scheduling")]
[HttpPut("/api/business-console/v1/scheduling/plans/{planId}/working-draft")]
[BusinessGatewayOperationId("saveBusinessConsoleSchedulingWorkingDraft")]
public sealed class SaveBusinessConsoleSchedulingWorkingDraftEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSaveSchedulingWorkingDraftRequest, SchedulingWorkingDraftContract>(auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(BusinessConsoleSaveSchedulingWorkingDraftRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSaveSchedulingWorkingDraftRequest request) => request.EnvironmentId;
    protected override Task<SchedulingWorkingDraftContract> ForwardAsync(BusinessConsoleSaveSchedulingWorkingDraftRequest request, string bearerToken, CancellationToken ct) =>
        scheduling.SaveWorkingDraftAsync(tokenProvider.BearerToken, request, RequireAuthorizedPrincipalId(), ct);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/working-drafts")]
[BusinessGatewayOperationId("listBusinessConsoleSchedulingWorkingDrafts")]
public sealed class ListBusinessConsoleSchedulingWorkingDraftsEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleListSchedulingWorkingDraftsRequest, IReadOnlyList<SchedulingWorkingDraftContract>>(auth, BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleListSchedulingWorkingDraftsRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleListSchedulingWorkingDraftsRequest request) => request.EnvironmentId;
    protected override Task<IReadOnlyList<SchedulingWorkingDraftContract>> ForwardAsync(BusinessConsoleListSchedulingWorkingDraftsRequest request, string bearerToken, CancellationToken ct) =>
        scheduling.ListWorkingDraftsAsync(tokenProvider.BearerToken, request, RequireAuthorizedPrincipalId(), ct);
}

[Tags("Business Console Scheduling")]
[HttpDelete("/api/business-console/v1/scheduling/plans/{planId}/working-draft")]
[BusinessGatewayOperationId("clearBusinessConsoleSchedulingWorkingDraft")]
public sealed class ClearBusinessConsoleSchedulingWorkingDraftEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleClearSchedulingWorkingDraftRequest, object?>(auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(BusinessConsoleClearSchedulingWorkingDraftRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleClearSchedulingWorkingDraftRequest request) => request.EnvironmentId;
    protected override async Task<object?> ForwardAsync(BusinessConsoleClearSchedulingWorkingDraftRequest request, string bearerToken, CancellationToken ct)
    {
        await scheduling.ClearWorkingDraftAsync(tokenProvider.BearerToken, request, RequireAuthorizedPrincipalId(), ct);
        return null;
    }
}
