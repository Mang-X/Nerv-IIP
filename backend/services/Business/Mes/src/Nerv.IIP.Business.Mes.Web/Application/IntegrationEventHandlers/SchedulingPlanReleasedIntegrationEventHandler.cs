using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(SchedulePlanReleasedIntegrationEventTopic.TopicName, ConsumerName)]
public sealed class SchedulePlanReleasedIntegrationEventHandlerForDispatch(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SchedulePlanReleasedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.schedule-plan-released-dispatch";

    private readonly IntegrationEventConsumerGuard<SchedulePlanReleasedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SchedulingIntegrationEventTypes.SchedulePlanReleased,
            SchedulingIntegrationEventVersions.V1));

    public async Task HandleAsync(
        SchedulePlanReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(SchedulePlanReleasedIntegrationEventTopic.TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        SchedulePlanReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        SchedulePlanReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!string.Equals(integrationEvent.Payload.PlanStatus, "released", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        foreach (var operation in integrationEvent.Payload.AffectedOperations
                     .OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationSequence)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            await UpsertOperationTaskAsync(integrationEvent, operation, cancellationToken);
        }
    }

    private async Task UpsertOperationTaskAsync(
        SchedulePlanReleasedIntegrationEvent integrationEvent,
        SchedulePlanAffectedOperationPayload operation,
        CancellationToken cancellationToken)
    {
        if (operation.EndUtc <= operation.StartUtc)
        {
            throw new KnownException($"Released schedule operation has an invalid time window, OperationId = {operation.OperationId}");
        }

        var task = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.OperationTaskIdValue == operation.OperationId,
            cancellationToken);
        if (task is null)
        {
            var hasWorkOrder = await dbContext.WorkOrders.AnyAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId &&
                    x.EnvironmentId == integrationEvent.EnvironmentId &&
                    x.WorkOrderIdValue == operation.WorkOrderId,
                cancellationToken);
            if (!hasWorkOrder)
            {
                throw new KnownException($"MES work order was not found for released schedule operation, WorkOrderId = {operation.WorkOrderId}");
            }

            task = OperationTask.Queue(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                operation.WorkOrderId,
                operation.OperationId,
                operation.OperationSequence,
                operation.WorkCenterId,
                [],
                operation.StartUtc,
                operation.EndUtc - operation.StartUtc);
            dbContext.OperationTasks.Add(task);
        }

        task.ApplyScheduleAssignment(
            operation.WorkCenterId,
            operation.ResourceId,
            operation.StartUtc,
            operation.EndUtc,
            integrationEvent.OccurredAtUtc);
    }
}

public static class SchedulePlanReleasedIntegrationEventTopic
{
    public const string TopicName = "Nerv.IIP.Contracts.Scheduling.SchedulePlanReleasedIntegrationEvent";
}
