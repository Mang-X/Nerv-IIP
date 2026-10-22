using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/plans/{planId}/downtime-impact")]
[BusinessGatewayOperationId("getBusinessConsoleSchedulingDowntimeImpact")]
public sealed class GetBusinessConsoleSchedulingDowntimeImpactEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, SchedulingDowntimeImpactResponse>(auth, BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => request.EnvironmentId;
    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";
    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => request.PlanId;
    protected override Task<SchedulingDowntimeImpactResponse> ForwardAsync(BusinessConsoleSchedulingPlanRequest request, string bearerToken, CancellationToken ct) =>
        scheduling.GetDowntimeImpactAsync(tokenProvider.BearerToken, request, ct);
}
