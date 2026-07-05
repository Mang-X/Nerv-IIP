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

        var deadLetters = new List<IntegrationEventDeadLetterMessage>();
        foreach (var operation in integrationEvent.Payload.AffectedOperations
                     .OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationSequence)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            var deadLetter = await UpsertOperationTaskAsync(integrationEvent, operation, cancellationToken);
            if (deadLetter is not null)
            {
                deadLetters.Add(deadLetter);
            }
        }

        if (deadLetters.Count > 0)
        {
            await deadLetterStore.AddRangeAsync(deadLetters, cancellationToken);
        }
    }

    private async Task<IntegrationEventDeadLetterMessage?> UpsertOperationTaskAsync(
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
                return IntegrationEventDeadLetterMessage.Create(
                        ConsumerName,
                        integrationEvent,
                        "mes.schedulePlanReleased.workOrderNotFound",
                        $"MES work order was not found for released schedule operation, WorkOrderId = {operation.WorkOrderId}, OperationId = {operation.OperationId}.");
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

        if (task.Status is OperationTaskLifecycleStatus.InProgress or OperationTaskLifecycleStatus.Paused)
        {
            return IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "mes.schedulePlanReleased.operationTaskInExecution",
                    $"MES operation task is already {task.Status} and cannot be overwritten by released schedule assignment, WorkOrderId = {operation.WorkOrderId}, OperationId = {operation.OperationId}, TargetWorkCenterId = {operation.WorkCenterId}, TargetResourceId = {operation.ResourceId}, TargetStartUtc = {operation.StartUtc:O}, TargetEndUtc = {operation.EndUtc:O}.");
        }

        // APS ResourceId is the selected executable device resource; MES stores that value as DeviceAssetId.
        task.ApplyScheduleAssignment(
            operation.WorkCenterId,
            operation.ResourceId,
            operation.StartUtc,
            operation.EndUtc,
            integrationEvent.OccurredAtUtc);
        return null;
    }
}

public static class SchedulePlanReleasedIntegrationEventTopic
{
    public const string TopicName = "Nerv.IIP.Contracts.Scheduling.SchedulePlanReleasedIntegrationEvent";
}

[IntegrationEventConsumer(SchedulePlanInvalidatedIntegrationEventTopic.TopicName, ConsumerName)]
public sealed class SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SchedulePlanInvalidatedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.schedule-plan-invalidated";

    private readonly IntegrationEventConsumerGuard<SchedulePlanInvalidatedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SchedulingIntegrationEventTypes.SchedulePlanInvalidated,
            SchedulingIntegrationEventVersions.V1));

    public async Task HandleAsync(
        SchedulePlanInvalidatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(SchedulePlanInvalidatedIntegrationEventTopic.TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        SchedulePlanInvalidatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        SchedulePlanInvalidatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var operationIds = integrationEvent.Payload.AffectedOperations
            .Select(x => x.OperationId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (operationIds.Length == 0)
        {
            return;
        }

        var tasks = await dbContext.OperationTasks
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                operationIds.Contains(x.OperationTaskIdValue))
            .ToArrayAsync(cancellationToken);

        foreach (var task in tasks)
        {
            task.MarkScheduleInvalidated();
        }
    }
}

public static class SchedulePlanInvalidatedIntegrationEventTopic
{
    public const string TopicName = "Nerv.IIP.Contracts.Scheduling.SchedulePlanInvalidatedIntegrationEvent";
}
