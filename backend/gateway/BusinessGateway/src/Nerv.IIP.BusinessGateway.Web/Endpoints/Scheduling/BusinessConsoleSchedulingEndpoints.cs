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
    IReadOnlyCollection<string> permissionCodes)
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(auth, permissionCodes)
    where TRequest : notnull
{
    protected AuthorizedBusinessSchedulingProxyEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        string permissionCode) : this(auth, [permissionCode])
    {
    }

    protected override JsonSerializerOptions? ResponseJsonOptions => SchedulingJson.Options;
}

internal static class OrderUrgencyReadPermissions
{
    public static readonly string[] All =
    [
        BusinessGatewayPermissions.SchedulingPlansRead,
        BusinessGatewayPermissions.ErpSalesRead,
        BusinessGatewayPermissions.PlanningDemandsRead,
        BusinessGatewayPermissions.MesWorkOrdersRead,
    ];
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
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => request.PlanId;

    protected override Task<SchedulePlanContract> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.GetPlanAsync(tokenProvider.BearerToken, request, cancellationToken);
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
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => request.PlanId;

    protected override Task<IReadOnlyCollection<GanttScheduleItemContract>> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.GetPlanGanttAsync(tokenProvider.BearerToken, request, cancellationToken);
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
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => request.PlanId;

    protected override Task<BusinessConsoleReleaseSchedulePlanResponse> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.ReleasePlanAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/plans/{planId}/revoke")]
[BusinessGatewayOperationId("revokeBusinessConsoleSchedulingPlan")]
public sealed class RevokeBusinessConsoleSchedulingPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, BusinessConsoleRevokeSchedulePlanResponse>(
        auth,
        BusinessGatewayPermissions.SchedulingPlansRelease)
{
    protected override string OrganizationId(BusinessConsoleSchedulingPlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleSchedulingPlanRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleSchedulingPlanRequest request) => "scheduling-plan";

    protected override string? ResourceId(BusinessConsoleSchedulingPlanRequest request) => request.PlanId;

    protected override Task<BusinessConsoleRevokeSchedulePlanResponse> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        scheduling.RevokePlanAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpPut("/api/business-console/v1/scheduling/plans/{planId}/operations/{operationId}/override")]
[BusinessGatewayOperationId("upsertBusinessConsoleSchedulingOperationOverride")]
public sealed class UpsertBusinessConsoleSchedulingOperationOverrideEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleScheduleOperationOverrideRequest, BusinessConsoleScheduleOperationOverrideResponse>(
        auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(BusinessConsoleScheduleOperationOverrideRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleScheduleOperationOverrideRequest request) => request.EnvironmentId;
    protected override string ResourceType(BusinessConsoleScheduleOperationOverrideRequest request) => "scheduling-operation";
    protected override string? ResourceId(BusinessConsoleScheduleOperationOverrideRequest request) => request.OperationId;
    protected override Task<BusinessConsoleScheduleOperationOverrideResponse> ForwardAsync(
        BusinessConsoleScheduleOperationOverrideRequest request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.UpsertOperationOverrideAsync(
            tokenProvider.BearerToken,
            request,
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/order-urgencies")]
[BusinessGatewayOperationId("listBusinessConsoleOrderUrgencies")]
public sealed class ListBusinessConsoleOrderUrgenciesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleOrderUrgencyListRequest, IReadOnlyCollection<OrderUrgencyContract>>(
        auth, OrderUrgencyReadPermissions.All)
{
    protected override string OrganizationId(BusinessConsoleOrderUrgencyListRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleOrderUrgencyListRequest request) => request.EnvironmentId;
    protected override Task<IReadOnlyCollection<OrderUrgencyContract>> ForwardAsync(
        BusinessConsoleOrderUrgencyListRequest request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.ListOrderUrgenciesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/order-urgencies/{orderReference}")]
[BusinessGatewayOperationId("getBusinessConsoleOrderUrgency")]
public sealed class GetBusinessConsoleOrderUrgencyEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleOrderUrgencyRequest, OrderUrgencyDetailContract>(
        auth, OrderUrgencyReadPermissions.All)
{
    protected override string OrganizationId(BusinessConsoleOrderUrgencyRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleOrderUrgencyRequest request) => request.EnvironmentId;
    protected override string ResourceType(BusinessConsoleOrderUrgencyRequest request) => "scheduling-order";
    protected override string? ResourceId(BusinessConsoleOrderUrgencyRequest request) => request.OrderReference;
    protected override Task<OrderUrgencyDetailContract> ForwardAsync(
        BusinessConsoleOrderUrgencyRequest request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.GetOrderUrgencyAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpPut("/api/business-console/v1/scheduling/order-urgencies/{orderReference}/business-priority")]
[BusinessGatewayOperationId("setBusinessConsoleOrderUrgencyBusinessPriority")]
public sealed class SetBusinessConsoleOrderUrgencyBusinessPriorityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSetOrderUrgencyBusinessPriorityRequest, OrderUrgencyDetailContract>(
        auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override string OrganizationId(BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request) => request.EnvironmentId;
    protected override string ResourceType(BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request) => "scheduling-order";
    protected override string? ResourceId(BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request) => request.OrderReference;
    protected override Task<OrderUrgencyDetailContract> ForwardAsync(
        BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.SetOrderUrgencyBusinessPriorityAsync(
            tokenProvider.BearerToken, request, RequireAuthorizedPrincipalActorReference(), cancellationToken);
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
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(0).When(x => x.PageIndex.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
    }
}

public sealed class BusinessConsoleSchedulingPlanRequestValidator : Validator<BusinessConsoleSchedulingPlanRequest>
{
    public BusinessConsoleSchedulingPlanRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(100).Must(NotBeWhiteSpace);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100).Must(NotBeWhiteSpace);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100).Must(NotBeWhiteSpace);
    }

    private static bool NotBeWhiteSpace(string value) => !string.IsNullOrWhiteSpace(value);
}

public sealed class BusinessConsoleScheduleOperationOverrideRequestValidator : Validator<BusinessConsoleScheduleOperationOverrideRequest>
{
    public BusinessConsoleScheduleOperationOverrideRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ResourceId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc);
    }
}

public sealed class BusinessConsoleOrderUrgencyListRequestValidator : Validator<BusinessConsoleOrderUrgencyListRequest>
{
    public BusinessConsoleOrderUrgencyListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderReferences).MaximumLength(4000);
    }
}

public sealed class BusinessConsoleOrderUrgencyRequestValidator : Validator<BusinessConsoleOrderUrgencyRequest>
{
    public BusinessConsoleOrderUrgencyRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderReference).NotEmpty().MaximumLength(128);
    }
}

public sealed class BusinessConsoleSetOrderUrgencyBusinessPriorityRequestValidator : Validator<BusinessConsoleSetOrderUrgencyBusinessPriorityRequest>
{
    private static readonly string[] Levels = ["p0", "p1", "p2", "p3"];

    public BusinessConsoleSetOrderUrgencyBusinessPriorityRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderReference).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Level).Must(value => Levels.Contains(value, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
