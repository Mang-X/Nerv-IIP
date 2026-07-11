using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.InventoryReservationExpiredIntegrationEvent", ConsumerName)]
public sealed class InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InventoryReservationExpiredIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.stock-reservation-expired";

    private readonly IntegrationEventConsumerGuard<InventoryReservationExpiredIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.StockReservationExpired,
            InventoryIntegrationEventVersions.V1));

    public Task HandleAsync(InventoryReservationExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(InventoryReservationExpiredIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InventoryReservationExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(InventoryReservationExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!IsWmsReservation(payload.ReservationSourceService))
        {
            return;
        }

        if (!await WmsProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OutboundOrderNo == payload.SourceDocumentId,
            cancellationToken);
        if (outbound is not null
            && outbound.Lines.Any(x => x.LineNo == payload.SourceDocumentLineId
                && string.Equals(x.InventoryReservationId, payload.ReservationId, StringComparison.Ordinal)))
        {
            outbound.MarkInventoryReservationReleased(payload.ReservationId);
            var openPickingTasks = await dbContext.WarehouseTasks
                .Where(x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.TaskType == WarehouseTaskType.Picking
                    && x.SourceOrderNo == payload.SourceDocumentId
                    && x.SourceOrderLineNo == payload.SourceDocumentLineId
                    && x.Status == WarehouseTaskStatus.Open)
                .ToArrayAsync(cancellationToken);
            foreach (var task in openPickingTasks)
            {
                task.Cancel();
            }

            var taskIds = openPickingTasks.Select(x => x.Id).ToArray();
            if (taskIds.Length > 0)
            {
                var openWcsTasks = await dbContext.WcsTasks
                    .Where(x => taskIds.Contains(x.WarehouseTaskId) && x.Status != WcsTaskStatus.Completed)
                    .ToArrayAsync(cancellationToken);
                foreach (var task in openWcsTasks)
                {
                    task.Cancel();
                }
            }
        }

        await dbContext.SaveEntitiesAsync(cancellationToken);
    }

    private static bool IsWmsReservation(string sourceService) =>
        string.Equals(sourceService, "wms", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceService, InventoryIntegrationEventSources.BusinessWms, StringComparison.OrdinalIgnoreCase);
}
