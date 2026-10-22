using FastEndpoints;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;

public sealed class MaintenanceDowntimeFactsRequestValidator : Validator<MaintenanceDowntimeFactsRequest>
{
    public MaintenanceDowntimeFactsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.DeviceAssetIds).NotEmpty();
        RuleForEach(x => x.DeviceAssetIds).NotEmpty().MaximumLength(150);
    }
}

public sealed class QueryMaintenanceDowntimeFactsEndpoint(ISender sender)
    : MaintenanceEndpoint<MaintenanceDowntimeFactsRequest, ResponseData<MaintenanceDowntimeFactsResponse>>
{
    public override void Configure() => ConfigureMaintenanceContract(MaintenanceEndpointContracts.Get<QueryMaintenanceDowntimeFactsEndpoint>());

    public override async Task HandleAsync(MaintenanceDowntimeFactsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryMaintenanceDowntimeFactsQuery(req), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}
