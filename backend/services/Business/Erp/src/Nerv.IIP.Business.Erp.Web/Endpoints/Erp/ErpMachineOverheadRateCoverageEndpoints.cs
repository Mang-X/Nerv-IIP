using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record GetMachineOverheadRatePeriodCoverageRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly Date);

public sealed class GetMachineOverheadRatePeriodCoverageEndpoint(ISender sender)
    : ErpEndpoint<GetMachineOverheadRatePeriodCoverageRequest, ResponseData<MachineOverheadRatePeriodCoverageResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetMachineOverheadRatePeriodCoverageEndpoint>());

    public override async Task HandleAsync(GetMachineOverheadRatePeriodCoverageRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMachineOverheadRatePeriodCoverageQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Date), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
