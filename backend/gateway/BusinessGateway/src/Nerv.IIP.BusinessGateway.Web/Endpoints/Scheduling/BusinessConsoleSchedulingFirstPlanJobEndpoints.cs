using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/workbench/first-plan-jobs")]
[BusinessGatewayOperationId("acceptBusinessConsoleSchedulingFirstPlanJob")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData<SchedulingFirstPlanJobContract>), StatusCodes.Status202Accepted)]
public sealed class AcceptBusinessConsoleSchedulingFirstPlanJobEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<SchedulingFirstPlanInputContract, SchedulingFirstPlanJobContract>(
        auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override int StatusCode => StatusCodes.Status202Accepted;
    protected override string OrganizationId(SchedulingFirstPlanInputContract request) => request.OrganizationId;
    protected override string EnvironmentId(SchedulingFirstPlanInputContract request) => request.EnvironmentId;
    protected override Task<SchedulingFirstPlanJobContract> ForwardAsync(
        SchedulingFirstPlanInputContract request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.AcceptFirstPlanJobAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/workbench/first-plan-jobs/{jobId}")]
[BusinessGatewayOperationId("getBusinessConsoleSchedulingFirstPlanJob")]
public sealed class GetBusinessConsoleSchedulingFirstPlanJobEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingFirstPlanJobRequest, SchedulingFirstPlanJobContract>(
        auth, BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleSchedulingFirstPlanJobRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSchedulingFirstPlanJobRequest request) => request.EnvironmentId;
    protected override Task<SchedulingFirstPlanJobContract> ForwardAsync(
        BusinessConsoleSchedulingFirstPlanJobRequest request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.GetFirstPlanJobAsync(tokenProvider.BearerToken, request, cancellationToken);
}
