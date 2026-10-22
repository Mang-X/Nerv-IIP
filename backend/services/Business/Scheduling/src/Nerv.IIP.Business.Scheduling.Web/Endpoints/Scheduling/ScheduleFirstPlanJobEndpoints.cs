using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public sealed record GetScheduleFirstPlanJobRequest([property: RouteParam] ScheduleFirstPlanJobId JobId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);

public sealed class AcceptScheduleFirstPlanJobEndpoint(ISender sender, ScheduleFirstPlanJobQueue queue)
    : SchedulingEndpoint<SchedulingFirstPlanInputContract, ResponseData<SchedulingFirstPlanJobContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<AcceptScheduleFirstPlanJobEndpoint>());
    public override async Task HandleAsync(SchedulingFirstPlanInputContract req, CancellationToken ct)
    {
        // Send returns after the acceptance UoW commits. Worker cancellation belongs to the host, not this request.
        var result = await sender.Send(new AcceptScheduleFirstPlanJobCommand(req), ct);
        queue.Enqueue(new ScheduleFirstPlanJobId(result.JobId));
        await Send.ResponseAsync(result.AsResponseData(), StatusCodes.Status202Accepted, ct);
    }
}
public sealed class GetScheduleFirstPlanJobEndpoint(ISender sender)
    : SchedulingEndpoint<GetScheduleFirstPlanJobRequest, ResponseData<SchedulingFirstPlanJobContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetScheduleFirstPlanJobEndpoint>());
    public override async Task HandleAsync(GetScheduleFirstPlanJobRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetScheduleFirstPlanJobQuery(req.JobId, req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class SchedulingFirstPlanInputValidator : Validator<SchedulingFirstPlanInputContract>
{
    public SchedulingFirstPlanInputValidator()
    {
        RuleFor(x => x.ContractVersion).Equal(1);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.HorizonEndUtc).GreaterThan(x => x.HorizonStartUtc);
        RuleFor(x => x.Orders).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(x => x.Count <= SchedulingFirstPlanJobLimits.MaxOrderCount)
            .Must(x => x.Select(y => y.WorkOrderId?.Trim()).Distinct(StringComparer.Ordinal).Count() == x.Count)
            .WithMessage("所选工单数量不得超过 500，且不得重复。");
        RuleForEach(x => x.Orders).ChildRules(order =>
        {
            order.RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(128);
            order.RuleFor(x => x.Priority).InclusiveBetween(0, 9999);
        });
    }
}
