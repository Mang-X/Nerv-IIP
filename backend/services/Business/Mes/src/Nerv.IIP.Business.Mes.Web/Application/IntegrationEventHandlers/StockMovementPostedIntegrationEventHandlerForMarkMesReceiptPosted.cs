using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
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

    [CapSubscribe(nameof(StockMovementPostedIntegrationEvent), Group = ConsumerName)]
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

        // 线边调拨两条腿分别是出库与入库，出库腿的回执同样要收，否则「双腿都过账」永远凑不齐。
        var isMaterialTransferLeg = MaterialIssueRequest.TryParseLegIdempotencyKey(
            integrationEvent.Payload.IdempotencyKey, out var transferToken, out var transferLeg);

        if (!isMaterialTransferLeg &&
            !string.Equals(integrationEvent.Payload.MovementType, "inbound", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (isMaterialTransferLeg)
        {
            await MarkMaterialTransferPostedAsync(integrationEvent, transferToken, transferLeg, cancellationToken);
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

        receipt.MarkInventoryPosted(
            integrationEvent.Payload.InventoryMovementId,
            integrationEvent.Payload.Quantity,
            integrationEvent.Payload.PostedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 线边调拨腿回执：只有两条腿都过账成功，已收数量才增加、状态才可能翻 Received —— 齐套因此不会先于库存实扣转绿（#1322）。
    /// </summary>
    private async Task MarkMaterialTransferPostedAsync(
        StockMovementPostedIntegrationEvent integrationEvent,
        string transferToken,
        MaterialTransferLeg transferLeg,
        CancellationToken cancellationToken)
    {
        var materialRequest = await dbContext.MaterialIssueRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.RequestNo == integrationEvent.Payload.SourceDocumentId,
            cancellationToken);
        if (materialRequest is null)
        {
            return;
        }

        materialRequest.MarkInventoryPosted(transferToken, transferLeg, integrationEvent.Payload.PostedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool MatchesReceipt(
        FinishedGoodsReceiptRequest receipt,
        StockMovementPostedPayload payload)
    {
        return string.Equals(receipt.SkuId, payload.SkuCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(receipt.UomCode, payload.UomCode, StringComparison.OrdinalIgnoreCase) &&
            payload.Quantity > 0m &&
            payload.Quantity <= receipt.RemainingQuantity + FinishedGoodsReceiptRequest.QuantityTolerance &&
            (string.IsNullOrWhiteSpace(receipt.ProducedLotNo) ||
                string.Equals(receipt.ProducedLotNo, payload.LotNo, StringComparison.OrdinalIgnoreCase));
    }
}
