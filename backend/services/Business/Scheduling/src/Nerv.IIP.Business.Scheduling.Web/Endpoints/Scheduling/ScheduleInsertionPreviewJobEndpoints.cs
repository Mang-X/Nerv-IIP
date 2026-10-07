using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public sealed record GetScheduleInsertionPreviewJobRequest([property: RouteParam] ScheduleInsertionPreviewJobId JobId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);

public sealed class AcceptScheduleInsertionPreviewJobEndpoint(ISender sender, ScheduleInsertionPreviewJobQueue queue)
    : SchedulingEndpoint<SchedulingInsertionPreviewRequestContract, ResponseData<SchedulingInsertionPreviewJobContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<AcceptScheduleInsertionPreviewJobEndpoint>());
    public override async Task HandleAsync(SchedulingInsertionPreviewRequestContract req, CancellationToken ct)
    {
        // Send returns after the acceptance UoW commits. Worker cancellation belongs to the host, not this request.
        var result = await sender.Send(new AcceptScheduleInsertionPreviewJobCommand(req), ct);
        queue.Enqueue(new ScheduleInsertionPreviewJobId(result.JobId));
        await Send.ResponseAsync(result.AsResponseData(), StatusCodes.Status202Accepted, ct);
    }
}
public sealed class GetScheduleInsertionPreviewJobEndpoint(ISender sender)
    : SchedulingEndpoint<GetScheduleInsertionPreviewJobRequest, ResponseData<SchedulingInsertionPreviewJobContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetScheduleInsertionPreviewJobEndpoint>());
    public override async Task HandleAsync(GetScheduleInsertionPreviewJobRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetScheduleInsertionPreviewJobQuery(req.JobId, req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class SchedulingInsertionPreviewInputValidator : Validator<SchedulingInsertionPreviewRequestContract>
{
    public SchedulingInsertionPreviewInputValidator()
    {
        RuleFor(x => x.ContractVersion).Equal(1);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(128);
    }
}
