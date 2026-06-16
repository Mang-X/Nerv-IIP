using DotNetCore.CAP;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForStockStatus(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.quality-inspection-result";

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.InspectionPassed,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.EventType == QualityIntegrationEventTypes.InspectionRejected)
        {
            await new IntegrationEventConsumerGuard<InspectionResultIntegrationEvent>(
                    new IntegrationEventEnvelopeValidator(),
                    deadLetterStore,
                    new IntegrationEventConsumerOptions(
                        ConsumerName,
                        QualityIntegrationEventTypes.InspectionRejected,
                        QualityIntegrationEventVersions.V1))
                .HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
            return;
        }

        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var stockRelease = integrationEvent.Payload.StockRelease;
        if (stockRelease is null)
        {
            return;
        }

        var targetQualityStatus = integrationEvent.EventType switch
        {
            QualityIntegrationEventTypes.InspectionPassed => "qualified",
            QualityIntegrationEventTypes.InspectionRejected => "quarantine",
            _ => throw new InvalidOperationException($"Unsupported Quality inspection event type '{integrationEvent.EventType}'."),
        };

        await PostTransferLegAsync(integrationEvent, stockRelease, stockRelease.SourceQualityStatus, -integrationEvent.Payload.InspectedQuantity, "source", cancellationToken);
        await PostTransferLegAsync(integrationEvent, stockRelease, targetQualityStatus, integrationEvent.Payload.InspectedQuantity, "target", cancellationToken);
    }

    private Task<PostStockMovementResult> PostTransferLegAsync(
        InspectionResultIntegrationEvent integrationEvent,
        StockReleaseDimensionPayload stockRelease,
        string qualityStatus,
        decimal quantity,
        string leg,
        CancellationToken cancellationToken)
    {
        return sender.Send(
            new PostStockMovementCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                "transfer",
                "quality",
                integrationEvent.Payload.SourceDocumentId,
                integrationEvent.Payload.InspectionRecordId,
                $"{integrationEvent.EventId}:{leg}",
                integrationEvent.Payload.SkuCode,
                stockRelease.UomCode,
                stockRelease.SiteCode,
                stockRelease.LocationCode,
                stockRelease.LotNo,
                stockRelease.SerialNo,
                qualityStatus,
                stockRelease.OwnerType,
                stockRelease.OwnerId,
                quantity),
            cancellationToken);
    }
}
