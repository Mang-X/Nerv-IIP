using DotNetCore.CAP;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.InventoryReservationReleaseRequestedIntegrationEvent", ConsumerName)]
public sealed class InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations(
    ILogger<InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations> logger,
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InventoryReservationReleaseRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.reservation-release-requested";

    private readonly IntegrationEventConsumerGuard<InventoryReservationReleaseRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.InventoryReservationReleaseRequested,
            InventoryIntegrationEventVersions.V1));

    public async Task HandleAsync(InventoryReservationReleaseRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(InventoryReservationReleaseRequestedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InventoryReservationReleaseRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InventoryReservationReleaseRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var result = await sender.Send(
            new ReleaseStockReservationsBySourceCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.ReservationSourceService,
                payload.SourceDocumentId,
                payload.SourceDocumentLineIds),
            cancellationToken);
        logger.LogInformation(
            "Released {ReleasedReservationCount} Inventory reservations for SourceService={SourceService}, SourceDocumentId={SourceDocumentId}, ReleasedQuantity={ReleasedQuantity}.",
            result.ReleasedReservationCount,
            payload.ReservationSourceService,
            payload.SourceDocumentId,
            result.ReleasedQuantity);
    }
}
