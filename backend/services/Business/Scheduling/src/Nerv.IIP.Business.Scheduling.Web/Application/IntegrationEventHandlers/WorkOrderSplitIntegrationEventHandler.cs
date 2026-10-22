using DotNetCore.CAP;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.WorkOrderSplitIntegrationEvent", ConsumerName)]
public sealed class WorkOrderSplitIntegrationEventHandlerForApplySplit(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender) : IIntegrationEventHandler<WorkOrderSplitIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.work-order-split";

    private readonly IntegrationEventConsumerGuard<WorkOrderSplitIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.WorkOrderSplit,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(WorkOrderSplitIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(WorkOrderSplitIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderSplitIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(WorkOrderSplitIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            await sender.Send(new ApplyWorkOrderSplitCommand(integrationEvent), cancellationToken);
        }
    }
}
