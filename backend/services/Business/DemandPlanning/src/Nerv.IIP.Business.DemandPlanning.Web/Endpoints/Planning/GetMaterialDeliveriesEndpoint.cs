using Nerv.IIP.Contracts.DemandPlanning;
using FastEndpoints;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;

namespace Nerv.IIP.Business.DemandPlanning.Web.Endpoints.Planning;

public sealed record GetMaterialDeliveriesRequest([property: RouteParam] MrpRunId RunId,
    string OrganizationId, string EnvironmentId, string? PlanId = null);
public sealed class GetMaterialDeliveriesEndpoint(ISender sender)
    : DemandPlanningEndpoint<GetMaterialDeliveriesRequest, ResponseData<MaterialDeliveriesResponse>>
{
    public override void Configure() => ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<GetMaterialDeliveriesEndpoint>());
    public override async Task HandleAsync(GetMaterialDeliveriesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMaterialDeliveriesQuery(req.OrganizationId, req.EnvironmentId, req.RunId, req.PlanId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
