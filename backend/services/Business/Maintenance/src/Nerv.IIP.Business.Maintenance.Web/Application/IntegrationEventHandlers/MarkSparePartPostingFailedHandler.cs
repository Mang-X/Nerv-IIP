using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostingFailedIntegrationEvent", ConsumerName)]
public sealed class MarkSparePartPostingFailedHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockMovementPostingFailedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-maintenance.stock-movement-posting-failed";

    private readonly IntegrationEventConsumerGuard<StockMovementPostingFailedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1));

    public Task HandleAsync(StockMovementPostingFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(StockMovementPostingFailedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(StockMovementPostingFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(StockMovementPostingFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (payload.SourceService != InventoryMovementSourceServices.Maintenance ||
            payload.MovementType != InventoryMovementTypes.Outbound ||
            !Guid.TryParse(payload.SourceDocumentLineId, out var lineGuid))
        {
            return;
        }

        var lineId = new SparePartLineId(lineGuid);
        var match = await (
            from line in dbContext.SparePartLines
            join order in dbContext.MaintenanceWorkOrders
                on EF.Property<MaintenanceWorkOrderId>(line, "MaintenanceWorkOrderId") equals order.Id
            where line.Id == lineId
                && order.OrganizationId == integrationEvent.OrganizationId
                && order.EnvironmentId == integrationEvent.EnvironmentId
            select new { Line = line, order.Id, order.WorkOrderNo })
            .SingleOrDefaultAsync(cancellationToken);
        if (match is null ||
            (payload.SourceDocumentId != match.WorkOrderNo && payload.SourceDocumentId != match.Id.ToString()) ||
            payload.IdempotencyKey != $"maintenance:{integrationEvent.OrganizationId}:{integrationEvent.EnvironmentId}:{match.Id}:{lineId}")
        {
            return;
        }

        if (await MaintenanceProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            match.Line.MarkInventoryPostingFailed(
                string.IsNullOrWhiteSpace(payload.FailureMessage) ? payload.FailureCode : payload.FailureMessage);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
