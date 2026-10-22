using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/plans/{planId}/time-context")]
[BusinessGatewayOperationId("getBusinessConsoleSchedulingTimeContext")]
public sealed class GetBusinessConsoleSchedulingTimeContextEndpoint(IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling, SchedulingTimeContextClient masterData, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, IReadOnlyCollection<SchedulingTimeContextItem>>(
        auth, BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest req) => req.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest req) => req.EnvironmentId;
    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest req) => "scheduling-plan";
    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest req) => req.PlanId;
    protected override async Task<IReadOnlyCollection<SchedulingTimeContextItem>> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest req, string bearerToken, CancellationToken ct)
    {
        var plan = await scheduling.GetPlanAsync(tokenProvider.BearerToken, req, ct);
        if (!string.Equals(plan.PlanId, req.PlanId, StringComparison.Ordinal))
            throw new InvalidOperationException("Scheduling returned a different plan.");
        var centers = plan.Assignments.Select(x => x.WorkCenterId)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return await masterData.ResolveAsync(tokenProvider.BearerToken, new SchedulingTimeContextRequest(
            req.OrganizationId, req.EnvironmentId, req.PlanId, RequireAuthorizedPrincipalId(), centers), ct);
    }
}
