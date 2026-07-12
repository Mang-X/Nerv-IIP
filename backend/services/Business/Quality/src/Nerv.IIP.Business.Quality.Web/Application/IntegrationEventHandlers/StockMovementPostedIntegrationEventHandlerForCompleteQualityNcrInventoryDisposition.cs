using DotNetCore.CAP;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostedIntegrationEventHandlerForCompleteQualityNcrInventoryDisposition(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockMovementPostedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.inventory-stock-movement-posted";

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
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, InventoryMovementSourceServices.Quality, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Guid.TryParse(payload.SourceDocumentId, out var ncrGuid))
        {
            return;
        }

        await sender.Send(
            new CompleteNonconformanceReportInventoryDispositionCommand(
                new NonconformanceReportId(ncrGuid),
                payload.InventoryMovementId,
                payload.MovementType,
                payload.QualityStatus,
                payload.Quantity),
            cancellationToken);
    }
}
