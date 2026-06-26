using DotNetCore.CAP;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostedIntegrationEventHandlerForCloseQualityNcrScrap(
    INonconformanceReportRepository repository,
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

    [CapSubscribe("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!IsQualityScrapAdjustment(payload))
        {
            return;
        }

        if (!Guid.TryParse(payload.SourceDocumentId, out var ncrGuid))
        {
            return;
        }

        var ncr = await repository.GetAsync(new NonconformanceReportId(ncrGuid), cancellationToken);
        if (ncr is null || ncr.DispositionType != QualityNcrDispositionTypes.Scrap)
        {
            return;
        }

        ncr.CompleteScrapDisposition(payload.InventoryMovementId);
    }

    private static bool IsQualityScrapAdjustment(StockMovementPostedPayload payload)
    {
        return string.Equals(payload.SourceService, "quality", StringComparison.OrdinalIgnoreCase)
            && string.Equals(payload.MovementType, "adjustment", StringComparison.OrdinalIgnoreCase)
            && string.Equals(payload.QualityStatus, "blocked", StringComparison.OrdinalIgnoreCase)
            && payload.Quantity < 0;
    }
}
