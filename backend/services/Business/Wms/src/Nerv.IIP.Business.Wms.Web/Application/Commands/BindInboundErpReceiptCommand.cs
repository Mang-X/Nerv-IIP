using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

public sealed record BindInboundErpReceiptCommand(ConfirmInboundReceiptCommand Confirmation, Guid BatchId)
    : ICommand<ConfirmInboundReceiptResult>;

public sealed class BindInboundErpReceiptCommandLock : ICommandLock<BindInboundErpReceiptCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(BindInboundErpReceiptCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CompleteInboundOrderCommandLock.ForInboundOrder(command.Confirmation.InboundOrderId));
    }
}

public sealed class BindInboundErpReceiptCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseAssignedResourceExecutionAuthorizer authorizer,
    IWmsPurchaseReceiptAcceptanceClient erp)
    : ICommandHandler<BindInboundErpReceiptCommand, ConfirmInboundReceiptResult>
{
    public async Task<ConfirmInboundReceiptResult> Handle(BindInboundErpReceiptCommand request, CancellationToken cancellationToken)
    {
        var inbound = await ConfirmInboundReceiptCommandHandler.LoadAuthorizedAsync(
            dbContext, authorizer, request.Confirmation, cancellationToken);
        if (inbound.ReceiptBatchId != request.BatchId)
        {
            throw new WmsIdempotencyConflictException();
        }

        if (inbound.ErpReceiptAcceptedVersion is { } acceptedVersion)
        {
            return new ConfirmInboundReceiptResult(inbound.Id, acceptedVersion);
        }

        if (inbound.Status != InboundOrderStatus.Open)
        {
            throw new WmsLifecycleConflictException("confirm-inbound-receipt", inbound.Status.ToString());
        }

        var receiptNo = await erp.GetOrAcceptAsync(inbound, request.BatchId, cancellationToken);
        inbound.BindErpPurchaseReceipt(request.BatchId, receiptNo);
        return new ConfirmInboundReceiptResult(inbound.Id, inbound.Version);
    }
}
