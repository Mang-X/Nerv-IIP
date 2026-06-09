using DotNetCore.CAP;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.InventoryMovementRequestedIntegrationEvent", ConsumerName)]
public sealed class InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InventoryMovementRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.movement-requested";

    private readonly IntegrationEventConsumerGuard<InventoryMovementRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1));

    public async Task HandleAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Inventory.InventoryMovementRequestedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InventoryMovementRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        await sender.Send(
            new PostStockMovementCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.MovementType,
                payload.SourceService,
                payload.SourceDocumentId,
                payload.SourceDocumentLineId,
                payload.IdempotencyKey,
                payload.SkuCode,
                payload.UomCode,
                payload.SiteCode,
                payload.LocationCode,
                payload.LotNo,
                payload.SerialNo,
                payload.QualityStatus,
                payload.OwnerType,
                payload.OwnerId,
                payload.Quantity),
            cancellationToken);
    }
}
