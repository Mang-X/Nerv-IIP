using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.quality-inspection-result";

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            [
                QualityIntegrationEventTypes.InspectionPassed,
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                QualityIntegrationEventTypes.InspectionRejected
            ],
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var targetStatus = integrationEvent.EventType switch
        {
            QualityIntegrationEventTypes.InspectionPassed => StockQualityStatus.Unrestricted,
            QualityIntegrationEventTypes.InspectionConditionalReleased => StockQualityStatus.Restricted,
            QualityIntegrationEventTypes.InspectionRejected => StockQualityStatus.Blocked,
            _ => throw new InvalidOperationException("Quality inspection event was not filtered by the consumer guard."),
        };

        if (await IsAlreadyProcessedAsync(integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        if (payload.StockRelease is not null)
        {
            var sourceStatus = StockQualityStatus.Normalize(payload.StockRelease.SourceQualityStatus);
            targetStatus = StockQualityStatus.Normalize(payload.StockRelease.TargetQualityStatus ?? targetStatus);
            if (sourceStatus != StockQualityStatus.Quality)
            {
                throw new KnownException("Quality inspection stock release can only transfer stock from quality status.");
            }

            await sender.Send(
                new PostStockStatusTransferCommand(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    sourceStatus,
                    targetStatus,
                    "quality",
                    payload.SourceDocumentId,
                    payload.InspectionRecordId,
                    integrationEvent.IdempotencyKey,
                    payload.SkuCode,
                    payload.StockRelease.UomCode,
                    payload.StockRelease.SiteCode,
                    payload.StockRelease.LocationCode,
                    payload.StockRelease.LotNo,
                    payload.StockRelease.SerialNo,
                    payload.StockRelease.OwnerType,
                    payload.StockRelease.OwnerId,
                    payload.InspectedQuantity),
                cancellationToken);
            return;
        }

        var candidates = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SkuCode == payload.SkuCode
                && x.QualityStatus == "quality"
                && x.OnHandQuantity >= payload.InspectedQuantity)
            .OrderBy(x => x.SiteCode)
            .ThenBy(x => x.LocationCode)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (candidates.Count != 1)
        {
            throw new KnownException("Quality inspection result cannot be applied automatically because Inventory could not resolve exactly one matching quality stock ledger.");
        }

        var ledger = candidates.Single();
        await sender.Send(
            new PostStockStatusTransferCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                "quality",
                targetStatus,
                "quality",
                payload.SourceDocumentId,
                payload.InspectionRecordId,
                integrationEvent.IdempotencyKey,
                ledger.SkuCode,
                ledger.UomCode,
                ledger.SiteCode,
                ledger.LocationCode,
                ledger.LotNo,
                ledger.SerialNo,
                ledger.OwnerType,
                ledger.OwnerId,
                payload.InspectedQuantity),
            cancellationToken);
    }

    private async Task<bool> IsAlreadyProcessedAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var outboundKey = $"{integrationEvent.IdempotencyKey}:out";
        var inboundKey = $"{integrationEvent.IdempotencyKey}:in";
        var outboundExists = await dbContext.StockMovements.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SourceService == "quality"
                && x.SourceDocumentId == payload.SourceDocumentId
                && x.IdempotencyKey == outboundKey,
            cancellationToken);
        if (!outboundExists)
        {
            return false;
        }

        return await dbContext.StockMovements.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SourceService == "quality"
                && x.SourceDocumentId == payload.SourceDocumentId
                && x.IdempotencyKey == inboundKey,
            cancellationToken);
    }
}
