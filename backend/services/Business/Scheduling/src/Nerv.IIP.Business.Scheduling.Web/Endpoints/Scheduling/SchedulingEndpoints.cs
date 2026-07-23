using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Web.Application.Auth;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public abstract class SchedulingEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureSchedulingContract(SchedulingEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            case "PUT":
                Put(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by Scheduling endpoints.");
        }

        Tags("Business Scheduling");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record PreviewSchedulePlanRequest(SchedulingProblemContract Problem);

public sealed record CreateSchedulePlanRequest(SchedulingProblemContract Problem);

public sealed record ListSchedulePlansRequest(
    string OrganizationId,
    string EnvironmentId,
    int? PageIndex = null,
    int? PageSize = null);

public sealed record GetSchedulePlanRequest(
    [property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record GetSchedulePlanGanttRequest(
    [property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record ReleaseSchedulePlanRequest(
    [property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record RevokeSchedulePlanRequest(
    [property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record UpsertScheduleOperationOverrideRequest(
    [property: RouteParam] string PlanId,
    [property: RouteParam] string OperationId,
    string OrganizationId,
    string EnvironmentId,
    string ResourceId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public sealed record ListOrderUrgenciesRequest(
    string OrganizationId,
    string EnvironmentId,
    string? OrderReferences = null);

public sealed record GetOrderUrgencyRequest(
    [property: RouteParam] string OrderReference,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record SetOrderUrgencyBusinessPriorityRequest(
    [property: RouteParam] string OrderReference,
    string OrganizationId,
    string EnvironmentId,
    string Level,
    string Reason,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record RestoreOrderUrgencyArchiveRequest(
    string OrganizationId,
    string EnvironmentId,
    string BatchId,
    string Reason);

public sealed class PreviewSchedulePlanEndpoint(ISender sender)
    : SchedulingEndpoint<PreviewSchedulePlanRequest, ResponseData<SchedulePlanContract>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<PreviewSchedulePlanEndpoint>());
    }

    public override async Task HandleAsync(PreviewSchedulePlanRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new PreviewSchedulePlanCommand(req.Problem), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateSchedulePlanEndpoint(ISender sender)
    : SchedulingEndpoint<CreateSchedulePlanRequest, ResponseData<SchedulePlanContract>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<CreateSchedulePlanEndpoint>());
    }

    public override async Task HandleAsync(CreateSchedulePlanRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new CreateSchedulePlanCommand(req.Problem), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class AssembleSchedulingProblemEndpoint(ISender sender)
    : SchedulingEndpoint<AssembleSchedulingProblemRequest, ResponseData<SchedulingProblemContract>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<AssembleSchedulingProblemEndpoint>());
    }

    public override async Task HandleAsync(AssembleSchedulingProblemRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new AssembleSchedulingProblemCommand(req), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListSchedulePlansEndpoint(ISender sender)
    : SchedulingEndpoint<ListSchedulePlansRequest, ResponseData<IReadOnlyCollection<SchedulePlanSummaryResponse>>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<ListSchedulePlansEndpoint>());
    }

    public override async Task HandleAsync(ListSchedulePlansRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListSchedulePlansQuery(req.OrganizationId, req.EnvironmentId, req.PageIndex, req.PageSize), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetSchedulePlanEndpoint(ISender sender)
    : SchedulingEndpoint<GetSchedulePlanRequest, ResponseData<SchedulePlanContract>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetSchedulePlanEndpoint>());
    }

    public override async Task HandleAsync(GetSchedulePlanRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetSchedulePlanDetailQuery(
            req.PlanId,
            req.OrganizationId,
            req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetSchedulePlanGanttEndpoint(ISender sender)
    : SchedulingEndpoint<GetSchedulePlanGanttRequest, ResponseData<IReadOnlyCollection<GanttScheduleItemContract>>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetSchedulePlanGanttEndpoint>());
    }

    public override async Task HandleAsync(GetSchedulePlanGanttRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetSchedulePlanGanttQuery(
            req.PlanId,
            req.OrganizationId,
            req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ReleaseSchedulePlanEndpoint(ISender sender)
    : SchedulingEndpoint<ReleaseSchedulePlanRequest, ResponseData<ReleaseSchedulePlanResponse>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<ReleaseSchedulePlanEndpoint>());
    }

    public override async Task HandleAsync(ReleaseSchedulePlanRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ReleaseSchedulePlanCommand(
            req.PlanId,
            req.OrganizationId,
            req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RevokeSchedulePlanEndpoint(ISender sender)
    : SchedulingEndpoint<RevokeSchedulePlanRequest, ResponseData<RevokeSchedulePlanResponse>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<RevokeSchedulePlanEndpoint>());
    }

    public override async Task HandleAsync(RevokeSchedulePlanRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new RevokeSchedulePlanCommand(
            req.PlanId,
            req.OrganizationId,
            req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class UpsertScheduleOperationOverrideEndpoint(ISender sender)
    : SchedulingEndpoint<UpsertScheduleOperationOverrideRequest, ResponseData<ScheduleOperationOverrideResponse>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<UpsertScheduleOperationOverrideEndpoint>());

    public override async Task HandleAsync(UpsertScheduleOperationOverrideRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new UpsertScheduleOperationOverrideCommand(
            req.OrganizationId, req.EnvironmentId, req.PlanId, req.OperationId,
            req.ResourceId, req.StartUtc, req.EndUtc), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListOrderUrgenciesEndpoint(ISender sender)
    : SchedulingEndpoint<ListOrderUrgenciesRequest, ResponseData<IReadOnlyCollection<OrderUrgencyContract>>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<ListOrderUrgenciesEndpoint>());

    public override async Task HandleAsync(ListOrderUrgenciesRequest req, CancellationToken ct)
    {
        var references = (req.OrderReferences ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var response = await sender.Send(new ListOrderUrgenciesQuery(req.OrganizationId, req.EnvironmentId, references), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetOrderUrgencyEndpoint(ISender sender)
    : SchedulingEndpoint<GetOrderUrgencyRequest, ResponseData<OrderUrgencyDetailContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetOrderUrgencyEndpoint>());

    public override async Task HandleAsync(GetOrderUrgencyRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetOrderUrgencyQuery(req.OrganizationId, req.EnvironmentId, req.OrderReference), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class SetOrderUrgencyBusinessPriorityEndpoint(
    ISender sender,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : SchedulingEndpoint<SetOrderUrgencyBusinessPriorityRequest, ResponseData<OrderUrgencyDetailContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<SetOrderUrgencyBusinessPriorityEndpoint>());

    public override async Task HandleAsync(SetOrderUrgencyBusinessPriorityRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new SetOrderUrgencyBusinessPriorityCommand(
            req.OrganizationId, req.EnvironmentId, req.OrderReference,
            Enum.Parse<BusinessPriorityLevel>(req.Level, true), contextAccessor.GetContext().Actor,
            req.Reason, req.ExpiresAtUtc), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RestoreOrderUrgencyArchiveEndpoint(
    OrderUrgencyRetentionService service,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : SchedulingEndpoint<RestoreOrderUrgencyArchiveRequest, ResponseData<OrderUrgencyRestoreResult>>
{
    public override void Configure() =>
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<RestoreOrderUrgencyArchiveEndpoint>());

    public override async Task HandleAsync(RestoreOrderUrgencyArchiveRequest req, CancellationToken ct)
    {
        var response = await service.RestoreAsync(
            req.OrganizationId,
            req.EnvironmentId,
            req.BatchId,
            contextAccessor.GetContext().Actor,
            req.Reason,
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListSchedulePlansRequestValidator : Validator<ListSchedulePlansRequest>
{
    public ListSchedulePlansRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(0).When(x => x.PageIndex.HasValue);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ListSchedulePlansQueryHandler.MaxPageSize)
            .When(x => x.PageSize.HasValue);
    }
}

public sealed class GetSchedulePlanRequestValidator : Validator<GetSchedulePlanRequest>
{
    public GetSchedulePlanRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetSchedulePlanGanttRequestValidator : Validator<GetSchedulePlanGanttRequest>
{
    public GetSchedulePlanGanttRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class ReleaseSchedulePlanRequestValidator : Validator<ReleaseSchedulePlanRequest>
{
    public ReleaseSchedulePlanRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class RevokeSchedulePlanRequestValidator : Validator<RevokeSchedulePlanRequest>
{
    public RevokeSchedulePlanRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpsertScheduleOperationOverrideRequestValidator : Validator<UpsertScheduleOperationOverrideRequest>
{
    public UpsertScheduleOperationOverrideRequestValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ResourceId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc);
    }
}

public sealed class ListOrderUrgenciesRequestValidator : Validator<ListOrderUrgenciesRequest>
{
    public ListOrderUrgenciesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderReferences).MaximumLength(4000);
    }
}

public sealed class GetOrderUrgencyRequestValidator : Validator<GetOrderUrgencyRequest>
{
    public GetOrderUrgencyRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderReference).NotEmpty().MaximumLength(128);
    }
}

public sealed class SetOrderUrgencyBusinessPriorityRequestValidator : Validator<SetOrderUrgencyBusinessPriorityRequest>
{
    public SetOrderUrgencyBusinessPriorityRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderReference).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Level).Must(value => new[] { "P0", "P1", "P2", "P3" }.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Level must be P0, P1, P2, or P3.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RestoreOrderUrgencyArchiveRequestValidator : Validator<RestoreOrderUrgencyArchiveRequest>
{
    public RestoreOrderUrgencyArchiveRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.BatchId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed record SchedulingEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class SchedulingEndpointContracts
{
    public static readonly IReadOnlyCollection<SchedulingEndpointContract> All =
    [
        new(typeof(PreviewSchedulePlanEndpoint), "POST", "/api/business/v1/scheduling/plans/preview", SchedulingPermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "previewSchedulingPlan"),
        new(typeof(CreateSchedulePlanEndpoint), "POST", "/api/business/v1/scheduling/plans", SchedulingPermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "createSchedulingPlan"),
        new(typeof(AssembleSchedulingProblemEndpoint), "POST", "/api/business/v1/scheduling/problems/assemble", SchedulingPermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "assembleSchedulingProblem"),
        new(typeof(ListSchedulePlansEndpoint), "GET", "/api/business/v1/scheduling/plans", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listSchedulingPlans"),
        new(typeof(GetSchedulePlanEndpoint), "GET", "/api/business/v1/scheduling/plans/{planId}", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "getSchedulingPlan"),
        new(typeof(GetSchedulePlanGanttEndpoint), "GET", "/api/business/v1/scheduling/plans/{planId}/gantt", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "getSchedulingPlanGantt"),
        new(typeof(ReleaseSchedulePlanEndpoint), "POST", "/api/business/v1/scheduling/plans/{planId}/release", SchedulingPermissionCodes.PlansRelease, InternalServiceAuthorizationPolicy.Name, "releaseSchedulingPlan"),
        new(typeof(RevokeSchedulePlanEndpoint), "POST", "/api/business/v1/scheduling/plans/{planId}/revoke", SchedulingPermissionCodes.PlansRelease, InternalServiceAuthorizationPolicy.Name, "revokeSchedulingPlan"),
        new(typeof(UpsertScheduleOperationOverrideEndpoint), "PUT", "/api/business/v1/scheduling/plans/{planId}/operations/{operationId}/override", SchedulingPermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "upsertSchedulingOperationOverride"),
        new(typeof(ListOrderUrgenciesEndpoint), "GET", "/api/business/v1/scheduling/order-urgencies", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listOrderUrgencies"),
        new(typeof(GetOrderUrgencyEndpoint), "GET", "/api/business/v1/scheduling/order-urgencies/{orderReference}", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "getOrderUrgency"),
        new(typeof(SetOrderUrgencyBusinessPriorityEndpoint), "PUT", "/api/business/v1/scheduling/order-urgencies/{orderReference}/business-priority", SchedulingPermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "setOrderUrgencyBusinessPriority"),
        new(typeof(RestoreOrderUrgencyArchiveEndpoint), "POST", "/api/business/internal/v1/scheduling/order-urgency-archives/restore", SchedulingPermissionCodes.PlansManage, InternalServiceAuthorizationPolicy.Name, "restoreOrderUrgencyArchive"),
    ];

    public static SchedulingEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out SchedulingEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
