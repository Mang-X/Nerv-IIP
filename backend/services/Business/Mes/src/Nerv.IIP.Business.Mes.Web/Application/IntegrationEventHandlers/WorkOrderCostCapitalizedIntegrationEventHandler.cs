using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.WorkOrderCostCapitalizedIntegrationEvent", ConsumerName)]
public sealed class WorkOrderCostCapitalizedIntegrationEventHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WorkOrderCostCapitalizedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.work-order-cost-capitalized";
    private readonly IntegrationEventConsumerGuard<WorkOrderCostCapitalizedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, ErpIntegrationEventTypes.WorkOrderCostCapitalized, ErpIntegrationEventVersions.V1));

    public Task HandleAsync(WorkOrderCostCapitalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidAsync, cancellationToken);

    [CapSubscribe(nameof(WorkOrderCostCapitalizedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderCostCapitalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidAsync(WorkOrderCostCapitalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, ErpIntegrationEventSources.BusinessErp, StringComparison.OrdinalIgnoreCase)) return;
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        var receipts = await dbContext.FinishedGoodsReceiptRequests
            .Where(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == integrationEvent.Payload.WorkOrderId)
            .ToListAsync(cancellationToken);
        if (receipts.Count == 0) throw new InvalidOperationException($"No MES finished-goods receipt exists for work order '{integrationEvent.Payload.WorkOrderId}'.");
        foreach (var receipt in receipts.Where(x => x.Status == FinishedGoodsReceiptRequest.RequestedStatus))
        {
            receipt.ApplyCapitalizedUnitCost(integrationEvent.Payload.UnitCost);
        }
    }
}
