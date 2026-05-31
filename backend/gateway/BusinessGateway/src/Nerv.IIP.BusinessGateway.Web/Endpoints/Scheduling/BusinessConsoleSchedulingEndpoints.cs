using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

public sealed record BusinessConsoleSchedulingProblemRequest(SchedulingProblemContract Problem);

public abstract class AuthorizedBusinessSchedulingProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode)
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(auth, permissionCode)
    where TRequest : notnull
{
    protected override JsonSerializerOptions? ResponseJsonOptions => SchedulingJson.Options;
}

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/plans/preview")]
[BusinessGatewayOperationId("previewBusinessConsoleSchedulingPlan")]
public sealed class PreviewBusinessConsoleSchedulingPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingProblemRequest, SchedulePlanContract>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(BusinessConsoleSchedulingProblemRequest request) => request.Problem.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingProblemRequest request) => request.Problem.EnvironmentId;

    protected override Task<SchedulePlanContract> ForwardAsync(
        BusinessConsoleSchedulingProblemRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.PreviewPlanAsync(tokenProvider.BearerToken, request.Problem, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/plans")]
[BusinessGatewayOperationId("createBusinessConsoleSchedulingPlan")]
public sealed class CreateBusinessConsoleSchedulingPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingProblemRequest, SchedulePlanContract>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(BusinessConsoleSchedulingProblemRequest request) => request.Problem.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingProblemRequest request) => request.Problem.EnvironmentId;

    protected override Task<SchedulePlanContract> ForwardAsync(
        BusinessConsoleSchedulingProblemRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.CreatePlanAsync(tokenProvider.BearerToken, request.Problem, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/plans")]
[BusinessGatewayOperationId("listBusinessConsoleSchedulingPlans")]
public sealed class ListBusinessConsoleSchedulingPlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingContextRequest, IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleSchedulingContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingContextRequest request) => request.EnvironmentId;

    protected override Task<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>> ForwardAsync(
        BusinessConsoleSchedulingContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.ListPlansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/plans/{planId}")]
[BusinessGatewayOperationId("getBusinessConsoleSchedulingPlan")]
public sealed class GetBusinessConsoleSchedulingPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, SchedulePlanContract>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => Query<string>("organizationId")!;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => Query<string>("environmentId")!;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => Route<string>("planId") ?? request.PlanId;

    protected override Task<SchedulePlanContract> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.GetPlanAsync(tokenProvider.BearerToken, WithRoutePlanId(request), cancellationToken);

    private BusinessConsoleSchedulingPlanRequest WithRoutePlanId(BusinessConsoleSchedulingPlanRequest request) =>
        request with
        {
            PlanId = Route<string>("planId") ?? request.PlanId,
            OrganizationId = Query<string>("organizationId")!,
            EnvironmentId = Query<string>("environmentId")!
        };
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/plans/{planId}/gantt")]
[BusinessGatewayOperationId("getBusinessConsoleSchedulingPlanGantt")]
public sealed class GetBusinessConsoleSchedulingPlanGanttEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, IReadOnlyCollection<GanttScheduleItemContract>>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => Query<string>("organizationId")!;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => Query<string>("environmentId")!;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => Route<string>("planId") ?? request.PlanId;

    protected override Task<IReadOnlyCollection<GanttScheduleItemContract>> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.GetPlanGanttAsync(tokenProvider.BearerToken, WithRoutePlanId(request), cancellationToken);

    private BusinessConsoleSchedulingPlanRequest WithRoutePlanId(BusinessConsoleSchedulingPlanRequest request) =>
        request with
        {
            PlanId = Route<string>("planId") ?? request.PlanId,
            OrganizationId = Query<string>("organizationId")!,
            EnvironmentId = Query<string>("environmentId")!
        };
}

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/plans/{planId}/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleSchedulingPlan")]
public sealed class ReleaseBusinessConsoleSchedulingPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, BusinessConsoleReleaseSchedulePlanResponse>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansRelease)
{
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => Query<string>("organizationId")!;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => Query<string>("environmentId")!;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => Route<string>("planId") ?? request.PlanId;

    protected override Task<BusinessConsoleReleaseSchedulePlanResponse> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.ReleasePlanAsync(tokenProvider.BearerToken, WithRoutePlanId(request), cancellationToken);

    private BusinessConsoleSchedulingPlanRequest WithRoutePlanId(BusinessConsoleSchedulingPlanRequest request) =>
        request with
        {
            PlanId = Route<string>("planId") ?? request.PlanId,
            OrganizationId = Query<string>("organizationId")!,
            EnvironmentId = Query<string>("environmentId")!
        };
}

public sealed class BusinessConsoleSchedulingProblemRequestValidator : Validator<BusinessConsoleSchedulingProblemRequest>
{
    public BusinessConsoleSchedulingProblemRequestValidator()
    {
        RuleFor(x => x.Problem).NotNull();
        When(x => x.Problem is not null, () =>
        {
            RuleFor(x => x.Problem!.OrganizationId).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Problem!.EnvironmentId).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Problem!.HorizonEndUtc).GreaterThanOrEqualTo(x => x.Problem!.HorizonStartUtc);
        });
    }
}

public sealed class BusinessConsoleSchedulingContextRequestValidator : Validator<BusinessConsoleSchedulingContextRequest>
{
    public BusinessConsoleSchedulingContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleSchedulingPlanRequestValidator : Validator<BusinessConsoleSchedulingPlanRequest>
{
    public BusinessConsoleSchedulingPlanRequestValidator()
    {
    }
}
