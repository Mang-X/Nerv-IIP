using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.InventoryReservationExpiredIntegrationEvent", ConsumerName)]
public sealed class InventoryReservationExpiredIntegrationEventHandlerForMarkMesRequestExpired(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InventoryReservationExpiredIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.stock-reservation-expired";

    private readonly IntegrationEventConsumerGuard<InventoryReservationExpiredIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.StockReservationExpired,
            InventoryIntegrationEventVersions.V1));

    public Task HandleAsync(InventoryReservationExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe("Nerv.IIP.Contracts.Inventory.InventoryReservationExpiredIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InventoryReservationExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(InventoryReservationExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!IsMesReservation(payload.ReservationSourceService))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var sourceDocumentIds = new[] { payload.SourceDocumentId, payload.SourceDocumentLineId }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var request = await dbContext.MaterialIssueRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && sourceDocumentIds.Contains(x.RequestNo),
            cancellationToken);
        request?.MarkInventoryReservationExpired(payload.ExpiresAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsMesReservation(string sourceService) =>
        string.Equals(sourceService, "mes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceService, InventoryIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase);
}
