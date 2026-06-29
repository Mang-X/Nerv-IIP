using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockMovementPostedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.stock-movement-posted";

    private readonly IntegrationEventConsumerGuard<StockMovementPostedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1));

    public async Task HandleAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.Payload.SourceService, InventoryIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(integrationEvent.Payload.MovementType, "inbound", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.RequestNo == integrationEvent.Payload.SourceDocumentId,
            cancellationToken);
        if (receipt is null)
        {
            return;
        }

        if (!MatchesReceipt(receipt, integrationEvent.Payload))
        {
            return;
        }

        receipt.MarkPosted(integrationEvent.Payload.InventoryMovementId, integrationEvent.Payload.PostedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool MatchesReceipt(
        FinishedGoodsReceiptRequest receipt,
        StockMovementPostedPayload payload)
    {
        // Inventory posts MES finished-goods receipt requests as a whole request.
        // A partial or adjusted quantity must not close the MES receipt silently.
        return string.Equals(receipt.SkuId, payload.SkuCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(receipt.UomCode, payload.UomCode, StringComparison.OrdinalIgnoreCase) &&
            receipt.Quantity == payload.Quantity &&
            (string.IsNullOrWhiteSpace(receipt.ProducedLotNo) ||
                string.Equals(receipt.ProducedLotNo, payload.LotNo, StringComparison.OrdinalIgnoreCase));
    }
}
