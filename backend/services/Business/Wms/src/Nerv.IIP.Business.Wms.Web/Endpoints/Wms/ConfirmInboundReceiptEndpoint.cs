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
    IReadOnlyCollection<InboundReceiptQuantity> Lines,
    decimal? ExchangeRate = null);

public sealed class ConfirmInboundReceiptEndpoint(ISender sender, IServiceScopeFactory scopeFactory)
    : WmsEndpoint<ConfirmInboundReceiptRequest, ResponseData<ConfirmInboundReceiptResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<ConfirmInboundReceiptEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(ConfirmInboundReceiptRequest req, CancellationToken ct)
    {
        var command = new ConfirmInboundReceiptCommand(
            req.InboundOrderId, req.OrganizationId, req.EnvironmentId, req.ActorPrincipalId,
            req.AuthorizedSiteCodes, req.ScopeKind, req.ScopeId, req.ExpectedVersion,
            req.IdempotencyKey, req.Lines, req.ExchangeRate);
        var result = await sender.Send(command, ct);
        if (result.PendingErpBatchId is { } batchId)
        {
            // Read the committed staging transaction in a fresh unit of work.
            await using var scope = scopeFactory.CreateAsyncScope();
            result = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new BindInboundErpReceiptCommand(command, batchId), ct);
        }

        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}
