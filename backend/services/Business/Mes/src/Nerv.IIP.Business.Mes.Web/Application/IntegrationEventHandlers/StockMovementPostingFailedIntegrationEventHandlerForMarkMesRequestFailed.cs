using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostingFailedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockMovementPostingFailedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.stock-movement-posting-failed";

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

    [CapSubscribe("Nerv.IIP.Contracts.Inventory.StockMovementPostingFailedIntegrationEvent", Group = ConsumerName)]
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

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.RequestNo == payload.SourceDocumentId,
            cancellationToken);
        if (receipt is not null)
        {
            receipt.MarkInventoryPostingFailed(payload.FailureCode, payload.FailureMessage, payload.FailedAtUtc);
            return;
        }

        var materialRequest = await dbContext.MaterialIssueRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && (x.RequestNo == payload.SourceDocumentId || x.RequestNo == payload.SourceDocumentLineId),
            cancellationToken);
        if (materialRequest is null)
        {
            return;
        }

        var rollbackQuantity = string.Equals(materialRequest.RequestNo, payload.SourceDocumentId, StringComparison.OrdinalIgnoreCase)
            ? Math.Abs(payload.Quantity)
            : 0m;
        materialRequest.MarkInventoryPostingFailed(
            rollbackQuantity,
            payload.FailureCode,
            payload.FailureMessage,
            payload.FailedAtUtc);
    }
}
