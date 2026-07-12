using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostingFailedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed>? logger = null)
    : IIntegrationEventHandler<StockMovementPostingFailedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.stock-movement-posting-failed";
    private const string MaterialIssueIdempotencyPrefix = "mes:material-issue:";
    private const string LineSideReceiptIdempotencyPrefix = "mes:line-side-receipt:";
    private const string ProductionConsumptionIdempotencyPrefix = "mes:production-consumption:";
    private const string FinishedGoodsReceiptIdempotencyPrefix = "mes:finished-goods-receipt:";
    private const string LineSideTransferRollbackPrefix = "mes:line-side-transfer:";

    private readonly IntegrationEventConsumerGuard<StockMovementPostingFailedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1));

    public async Task HandleAsync(StockMovementPostingFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(StockMovementPostingFailedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(StockMovementPostingFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(StockMovementPostingFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, InventoryIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (IsFinishedGoodsReceipt(payload))
        {
            await MarkFinishedGoodsReceiptFailedAsync(integrationEvent, cancellationToken);
            return;
        }

        if (IsProductionConsumption(payload))
        {
            await MarkProductionConsumptionFailedAsync(integrationEvent, cancellationToken);
            return;
        }

        if (IsMaterialTransferLeg(payload))
        {
            await MarkMaterialTransferFailedAsync(integrationEvent, cancellationToken);
            return;
        }

        LogUnmatched(integrationEvent, "unknown MES Inventory movement idempotency key");
    }

    private async Task MarkFinishedGoodsReceiptFailedAsync(
        StockMovementPostingFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.RequestNo == payload.SourceDocumentId,
            cancellationToken);
        if (receipt is null)
        {
            LogUnmatched(integrationEvent, "finished goods receipt request was not found");
            return;
        }

        if (receipt.Status == FinishedGoodsReceiptRequest.PostedStatus ||
            receipt.Status == FinishedGoodsReceiptRequest.CancelledStatus)
        {
            return;
        }

        receipt.MarkInventoryPostingFailed(payload.FailureCode, payload.FailureMessage, payload.FailedAtUtc);
    }

    private async Task MarkProductionConsumptionFailedAsync(
        StockMovementPostingFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var consumption = await dbContext.ProductionReportMaterialConsumptions.FirstOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.ReportNo == payload.SourceDocumentId
                && x.MaterialIssueRequestNo == payload.SourceDocumentLineId
                && x.MaterialId == payload.SkuCode
                && x.MaterialLotId == payload.LotNo,
            cancellationToken);
        if (consumption is null)
        {
            LogUnmatched(integrationEvent, "production consumption was not found");
            return;
        }

        consumption.MarkInventoryPostingFailed(payload.FailureCode, payload.FailureMessage, payload.FailedAtUtc);
    }

    private async Task MarkMaterialTransferFailedAsync(
        StockMovementPostingFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var materialRequest = await dbContext.MaterialIssueRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.RequestNo == payload.SourceDocumentId,
            cancellationToken);
        if (materialRequest is null)
        {
            LogUnmatched(integrationEvent, "material issue request was not found");
            return;
        }

        materialRequest.MarkInventoryPostingFailed(
            Math.Abs(payload.Quantity),
            payload.FailureCode,
            payload.FailureMessage,
            payload.FailedAtUtc,
            NormalizeLineSideTransferRollbackKey(payload.IdempotencyKey));
    }

    private static bool IsMaterialTransferLeg(StockMovementPostingFailedPayload payload)
    {
        return payload.IdempotencyKey.StartsWith(MaterialIssueIdempotencyPrefix, StringComparison.OrdinalIgnoreCase) ||
            payload.IdempotencyKey.StartsWith(LineSideReceiptIdempotencyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductionConsumption(StockMovementPostingFailedPayload payload)
    {
        return payload.IdempotencyKey.StartsWith(ProductionConsumptionIdempotencyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFinishedGoodsReceipt(StockMovementPostingFailedPayload payload)
    {
        return payload.IdempotencyKey.StartsWith(FinishedGoodsReceiptIdempotencyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineSideTransferRollbackKey(string idempotencyKey)
    {
        if (idempotencyKey.StartsWith(MaterialIssueIdempotencyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return LineSideTransferRollbackPrefix + idempotencyKey[MaterialIssueIdempotencyPrefix.Length..];
        }

        if (idempotencyKey.StartsWith(LineSideReceiptIdempotencyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return LineSideTransferRollbackPrefix + idempotencyKey[LineSideReceiptIdempotencyPrefix.Length..];
        }

        return idempotencyKey;
    }

    private void LogUnmatched(StockMovementPostingFailedIntegrationEvent integrationEvent, string reason)
    {
        logger?.LogWarning(
            "MES Inventory posting failure event was not applied. Reason={Reason}, EventId={EventId}, MovementType={MovementType}, SourceDocumentId={SourceDocumentId}, SourceDocumentLineId={SourceDocumentLineId}, IdempotencyKey={IdempotencyKey}",
            reason,
            integrationEvent.EventId,
            integrationEvent.Payload.MovementType,
            integrationEvent.Payload.SourceDocumentId,
            integrationEvent.Payload.SourceDocumentLineId,
            integrationEvent.Payload.IdempotencyKey);
    }
}
