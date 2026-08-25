using FastEndpoints;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Resolutions;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;

public sealed record ResolveBarcodeRequest(
    string OrganizationId,
    string EnvironmentId,
    string ScannedValue,
    int Skip = 0,
    int Take = 20);

public sealed record ResolveBarcodeResponse(
    string Status,
    string? ReasonCode,
    IReadOnlyCollection<ResolvedBarcodeCandidate> Candidates,
    int Total);

public sealed class ResolveBarcodeEndpoint(ISender sender)
    : BarcodeLabelEndpoint<ResolveBarcodeRequest, ResponseData<ResolveBarcodeResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<ResolveBarcodeEndpoint>());
    }

    public override async Task HandleAsync(ResolveBarcodeRequest req, CancellationToken ct)
    {
        var result = await sender.Send(
            new ResolveBarcodeQuery(req.OrganizationId, req.EnvironmentId, req.ScannedValue, req.Skip, req.Take),
            ct);
        await Send.OkAsync(
            new ResolveBarcodeResponse(result.Status, result.ReasonCode, result.Candidates, result.Total).AsResponseData(),
            cancellation: ct);
    }
}
