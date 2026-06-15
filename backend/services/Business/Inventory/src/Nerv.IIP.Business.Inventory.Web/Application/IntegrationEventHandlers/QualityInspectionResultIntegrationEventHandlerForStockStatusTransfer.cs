using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
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

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _ = deadLetterStore;

        var targetStatus = integrationEvent.EventType switch
        {
            QualityIntegrationEventTypes.InspectionPassed => "unrestricted",
            QualityIntegrationEventTypes.InspectionRejected => "blocked",
            _ => throw new KnownException($"Quality inspection event type '{integrationEvent.EventType}' is not supported by Inventory stock release."),
        };

        if (integrationEvent.EventVersion != QualityIntegrationEventVersions.V1)
        {
            throw new KnownException($"Quality inspection event version '{integrationEvent.EventVersion}' is not supported by Inventory.");
        }

        var payload = integrationEvent.Payload;
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

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }
}
