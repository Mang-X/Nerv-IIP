using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.WorkOrderCostCapitalizedIntegrationEvent", ConsumerName)]
public sealed class WorkOrderCostCapitalizedIntegrationEventHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ITransactionUnitOfWork unitOfWork,
    IMesWorkOrderCapitalizationScopeCoordinator scopeCoordinator)
    : IIntegrationEventHandler<WorkOrderCostCapitalizedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.work-order-cost-capitalized";
    private readonly IntegrationEventConsumerGuard<WorkOrderCostCapitalizedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, ErpIntegrationEventTypes.WorkOrderCostCapitalized, ErpIntegrationEventVersions.V1));

    public WorkOrderCostCapitalizedIntegrationEventHandler(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        ITransactionUnitOfWork unitOfWork)
        : this(
            dbContext,
            deadLetterStore,
            unitOfWork,
            new PostgreSqlMesWorkOrderCapitalizationScopeCoordinator(dbContext, unitOfWork))
    {
    }

    public Task HandleAsync(WorkOrderCostCapitalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidAsync, cancellationToken);

    [CapSubscribe(nameof(WorkOrderCostCapitalizedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderCostCapitalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidAsync(WorkOrderCostCapitalizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, ErpIntegrationEventSources.BusinessErp, StringComparison.OrdinalIgnoreCase)) return;
        await scopeCoordinator.ExecuteAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.WorkOrderId,
            token => ApplyCapitalizationAsync(integrationEvent, token),
            cancellationToken);
    }

    private async Task ApplyCapitalizationAsync(
        WorkOrderCostCapitalizedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.WorkOrderIdValue == integrationEvent.Payload.WorkOrderId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"No MES work order exists for capitalized cost '{integrationEvent.Payload.WorkOrderId}'.");
        var receipts = await dbContext.FinishedGoodsReceiptRequests
            .Where(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == integrationEvent.Payload.WorkOrderId)
            .ToListAsync(cancellationToken);
        try
        {
            workOrder.ApplyCapitalizedUnitCost(integrationEvent.Payload.UnitCost);
            foreach (var receipt in receipts.Where(x => x.Status == FinishedGoodsReceiptRequest.RequestedStatus))
            {
                receipt.ApplyCapitalizedUnitCost(integrationEvent.Payload.UnitCost);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Aggregate validation can reject after earlier tracked aggregates were changed.
            // Rebuild only the terminal reliability facts so no partial cost or outbox state is saved.
            dbContext.ChangeTracker.Clear();
            if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(
                    dbContext,
                    ConsumerName,
                    integrationEvent,
                    cancellationToken))
            {
                return;
            }

            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "work-order-capitalization-divergence",
                    exception.Message),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
    }
}
