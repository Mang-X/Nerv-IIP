using DotNetCore.CAP;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostingFailedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockMovementPostingFailedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.stock-movement-posting-failed";

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
        if (!string.Equals(integrationEvent.Payload.SourceService, "wms", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await sender.Send(
            new MarkInventoryMovementRequestFailedCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.Payload.MovementType,
                integrationEvent.Payload.SourceDocumentId,
                integrationEvent.Payload.SourceDocumentLineId,
                integrationEvent.Payload.IdempotencyKey,
                integrationEvent.Payload.FailureCode,
                integrationEvent.Payload.FailureMessage),
            cancellationToken);
    }
}
