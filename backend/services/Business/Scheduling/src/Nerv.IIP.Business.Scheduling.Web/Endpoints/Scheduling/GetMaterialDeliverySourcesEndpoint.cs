using FastEndpoints;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public sealed record GetMaterialDeliverySourcesRequest(
    [property: RouteParam] string PlanId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<MaterialDeliverySourceSelection> Sources);

public sealed class GetMaterialDeliverySourcesEndpoint(ISender sender)
    : SchedulingEndpoint<GetMaterialDeliverySourcesRequest, ResponseData<MaterialDeliverySourcesResponse>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<GetMaterialDeliverySourcesEndpoint>());

    public override async Task HandleAsync(GetMaterialDeliverySourcesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMaterialDeliverySourcesQuery(req.PlanId, req.OrganizationId, req.EnvironmentId, req.Sources), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
