using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Web.Application.Auth;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;
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
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by Scheduling endpoints.");
        }

        Tags("Business Scheduling");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record PreviewSchedulePlanRequest(SchedulingProblemContract Problem);

public sealed record CreateSchedulePlanRequest(SchedulingProblemContract Problem);

public sealed record ListSchedulePlansRequest(string OrganizationId, string EnvironmentId);

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

public sealed class ListSchedulePlansEndpoint(ISender sender)
    : SchedulingEndpoint<ListSchedulePlansRequest, ResponseData<IReadOnlyCollection<SchedulePlanSummaryResponse>>>
{
    public override void Configure()
    {
        ConfigureSchedulingContract(SchedulingEndpointContracts.Get<ListSchedulePlansEndpoint>());
    }

    public override async Task HandleAsync(ListSchedulePlansRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListSchedulePlansQuery(req.OrganizationId, req.EnvironmentId), ct);
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
        var planId = Route<string>("planId") ?? req.PlanId;
        var response = await sender.Send(new GetSchedulePlanDetailQuery(
            planId,
            Query<string>("organizationId")!,
            Query<string>("environmentId")!), ct);
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
        var planId = Route<string>("planId") ?? req.PlanId;
        var response = await sender.Send(new GetSchedulePlanGanttQuery(
            planId,
            Query<string>("organizationId")!,
            Query<string>("environmentId")!), ct);
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
        var planId = Route<string>("planId") ?? req.PlanId;
        var response = await sender.Send(new ReleaseSchedulePlanCommand(
            planId,
            Query<string>("organizationId")!,
            Query<string>("environmentId")!), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
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
        new(typeof(ListSchedulePlansEndpoint), "GET", "/api/business/v1/scheduling/plans", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "listSchedulingPlans"),
        new(typeof(GetSchedulePlanEndpoint), "GET", "/api/business/v1/scheduling/plans/{planId}", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "getSchedulingPlan"),
        new(typeof(GetSchedulePlanGanttEndpoint), "GET", "/api/business/v1/scheduling/plans/{planId}/gantt", SchedulingPermissionCodes.PlansRead, InternalServiceAuthorizationPolicy.Name, "getSchedulingPlanGantt"),
        new(typeof(ReleaseSchedulePlanEndpoint), "POST", "/api/business/v1/scheduling/plans/{planId}/release", SchedulingPermissionCodes.PlansRelease, InternalServiceAuthorizationPolicy.Name, "releaseSchedulingPlan"),
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
