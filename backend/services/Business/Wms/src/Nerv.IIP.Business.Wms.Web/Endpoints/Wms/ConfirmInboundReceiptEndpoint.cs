using FastEndpoints;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;

namespace Nerv.IIP.Business.Wms.Web.Endpoints.Wms;

/// <summary>提交待收货入库单逐行核实的实际收货数量。</summary>
public sealed record ConfirmInboundReceiptRequest(
    [property: RouteParam] InboundOrderId InboundOrderId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    long ExpectedVersion,
    string IdempotencyKey,
    IReadOnlyCollection<InboundReceiptQuantity> Lines);

public sealed class ConfirmInboundReceiptEndpoint(ISender sender)
    : WmsEndpoint<ConfirmInboundReceiptRequest, ResponseData<ConfirmInboundReceiptResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<ConfirmInboundReceiptEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(ConfirmInboundReceiptRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmInboundReceiptCommand(
            req.InboundOrderId, req.OrganizationId, req.EnvironmentId, req.ActorPrincipalId,
            req.AuthorizedSiteCodes, req.ScopeKind, req.ScopeId, req.ExpectedVersion,
            req.IdempotencyKey, req.Lines), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}
