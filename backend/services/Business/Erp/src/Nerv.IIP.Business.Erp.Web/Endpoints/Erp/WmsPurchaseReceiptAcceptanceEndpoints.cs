using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record AcceptWmsPurchaseReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    Guid WmsReceiptBatchId,
    string PurchaseOrderNo,
    string SiteCode,
    decimal ExchangeRate,
    IReadOnlyCollection<WmsPurchaseReceiptActualLine> Lines,
    bool Unrestricted = false);

public sealed class AcceptWmsPurchaseReceiptEndpoint(ISender sender, IErpIntegrationEventContextAccessor eventContext)
    : ErpEndpoint<AcceptWmsPurchaseReceiptRequest, ResponseData<WmsPurchaseReceiptAcceptanceResponse>>
{
    public override void Configure() =>
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<AcceptWmsPurchaseReceiptEndpoint>());

    public override async Task HandleAsync(AcceptWmsPurchaseReceiptRequest req, CancellationToken ct)
    {
        using var causationScope = eventContext.BeginScope(ErpCommandCausationIds.ForHttpCommand(
            "accept-wms-purchase-receipt", req.OrganizationId, req.EnvironmentId, req.WmsReceiptBatchId.ToString("N")));

        try
        {
            await sender.Send(new AcceptWmsPurchaseReceiptCommand(
                req.OrganizationId, req.EnvironmentId, req.WmsReceiptBatchId, req.PurchaseOrderNo,
                req.SiteCode, req.ExchangeRate, req.Lines, req.Unrestricted), ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            AddError("采购订单在收货提交期间已变更，请使用原批次重新提交。");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }
        catch (DbUpdateException exception) when (ErpPersistenceConflicts.IsCodeIdempotencyKeyConflict(exception))
        {
            AddError("同一收货批次已经并发提交，请使用原批次重新提交。");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        var result = await sender.Send(new GetWmsPurchaseReceiptAcceptanceQuery(
            req.OrganizationId, req.EnvironmentId, req.WmsReceiptBatchId), ct)
            ?? throw new InvalidOperationException("Committed WMS purchase receipt acceptance was not found.");
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed record GetWmsPurchaseReceiptAcceptanceRequest(
    string OrganizationId,
    string EnvironmentId,
    Guid WmsReceiptBatchId);

public sealed class GetWmsPurchaseReceiptAcceptanceEndpoint(ISender sender)
    : ErpEndpoint<GetWmsPurchaseReceiptAcceptanceRequest, ResponseData<WmsPurchaseReceiptAcceptanceResponse>>
{
    public override void Configure() =>
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<GetWmsPurchaseReceiptAcceptanceEndpoint>());

    public override async Task HandleAsync(GetWmsPurchaseReceiptAcceptanceRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetWmsPurchaseReceiptAcceptanceQuery(
            req.OrganizationId, req.EnvironmentId, req.WmsReceiptBatchId), ct);
        if (result is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}
