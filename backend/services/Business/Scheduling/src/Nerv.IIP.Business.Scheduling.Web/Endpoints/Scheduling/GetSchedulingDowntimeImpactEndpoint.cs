using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public sealed record GetSchedulingDowntimeImpactRequest([property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);

public sealed class GetSchedulingDowntimeImpactEndpoint(ISender sender)
    : SchedulingEndpoint<GetSchedulingDowntimeImpactRequest, ResponseData<SchedulingDowntimeImpactResponse>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetSchedulingDowntimeImpactEndpoint>());
    public override async Task HandleAsync(GetSchedulingDowntimeImpactRequest req, CancellationToken ct) =>
        await Send.OkAsync((await sender.Send(new GetSchedulingDowntimeImpactQuery(req.PlanId, req.OrganizationId, req.EnvironmentId), ct))
            .AsResponseData(), cancellation: ct);
}
