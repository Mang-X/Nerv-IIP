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
    IIntegrationEventDeadLetterStore deadLetterStore,
    IMesScheduleReleaseScopeCoordinator scopeCoordinator)
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
        await scopeCoordinator.ExecuteAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            ct => HandleValidEventCoreAsync(integrationEvent, ct),
            cancellationToken);
    }

    private async Task HandleValidEventCoreAsync(
        SchedulePlanReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!string.Equals(integrationEvent.Payload.PlanStatus, "released", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PlanId) ||
            integrationEvent.Payload.ReleaseRevision is not > 0 ||
            integrationEvent.Payload.AffectedOperations is null)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "mes.schedulePlanReleased.invalidPayload",
                    "Released schedule requires a plan id, positive release revision, and affected operations collection."),
                cancellationToken);
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var watermark = await dbContext.ScheduleReleaseWatermarks.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId,
            cancellationToken);
        if (watermark is not null && watermark.RevokedReleaseRevision >= integrationEvent.Payload.ReleaseRevision)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "mes.schedulePlanReleased.releaseAlreadyRevoked",
                    $"Released schedule revision {integrationEvent.Payload.ReleaseRevision} is not newer than MES revoked watermark {watermark.RevokedReleaseRevision}."),
                cancellationToken);
            return;
        }

        var releasedOperationIds = integrationEvent.Payload.AffectedOperations
            .Select(x => x.OperationId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var omittedLegacyTasks = await dbContext.OperationTasks
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SchedulePlanId == null &&
                x.ScheduleReleaseRevision == null &&
                x.ScheduledAtUtc != null &&
                !releasedOperationIds.Contains(x.OperationTaskIdValue))
            .ToArrayAsync(cancellationToken);
        foreach (var legacyTask in omittedLegacyTasks)
        {
            legacyTask.ReconcileLegacyScheduleAssignment("superseded");
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
        if (string.IsNullOrWhiteSpace(operation.OperationId) ||
            string.IsNullOrWhiteSpace(operation.WorkCenterId))
        {
            return IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                "mes.schedulePlanReleased.invalidOperationIdentity",
                $"Released schedule operation requires an operation id and work center id, WorkOrderId = {operation.WorkOrderId}, OperationId = {operation.OperationId}, WorkCenterId = {operation.WorkCenterId}.");
        }

        if (operation.EndUtc <= operation.StartUtc)
        {
            return IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                "mes.schedulePlanReleased.invalidTimeWindow",
                $"Released schedule operation has an invalid time window, OperationId = {operation.OperationId}, StartUtc = {operation.StartUtc:O}, EndUtc = {operation.EndUtc:O}.");
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
                operation.EndUtc - operation.StartUtc,
                operationCode: operation.StandardOperationCode);
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

        if (task.Status is OperationTaskLifecycleStatus.Completed or OperationTaskLifecycleStatus.Cancelled)
        {
            return IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                "mes.schedulePlanReleased.operationTaskClosed",
                $"MES operation task is already {task.Status}; released schedule assignment was ignored, WorkOrderId = {operation.WorkOrderId}, OperationId = {operation.OperationId}.");
        }

        // APS ResourceId is the selected executable device resource; MES stores that value as DeviceAssetId.
        task.ApplyScheduleAssignment(
            operation.WorkCenterId,
            operation.ResourceId,
            operation.StartUtc,
            operation.EndUtc,
            integrationEvent.OccurredAtUtc,
            operation.StandardOperationCode,
            integrationEvent.Payload.PlanId,
            integrationEvent.Payload.ReleaseRevision);
        return null;
    }
}

public static class SchedulePlanReleasedIntegrationEventTopic
{
    public const string TopicName = "SchedulePlanReleasedIntegrationEvent";
}

[IntegrationEventConsumer(SchedulePlanRevokedIntegrationEventTopic.TopicName, ConsumerName)]
public sealed class SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IMesScheduleReleaseScopeCoordinator scopeCoordinator)
    : IIntegrationEventHandler<SchedulePlanRevokedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.schedule-plan-revoked-withdraw-dispatch";

    private readonly IntegrationEventConsumerGuard<SchedulePlanRevokedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SchedulingIntegrationEventTypes.SchedulePlanRevoked,
            SchedulingIntegrationEventVersions.V1));

    public Task HandleAsync(SchedulePlanRevokedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(SchedulePlanRevokedIntegrationEventTopic.TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(SchedulePlanRevokedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        SchedulePlanRevokedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await scopeCoordinator.ExecuteAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            ct => HandleValidEventCoreAsync(integrationEvent, ct),
            cancellationToken);
    }

    private async Task HandleValidEventCoreAsync(
        SchedulePlanRevokedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var isExplicit = string.Equals(integrationEvent.Payload.Reason, "explicit", StringComparison.Ordinal);
        var isSuperseded = string.Equals(integrationEvent.Payload.Reason, "superseded", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PlanId) ||
            integrationEvent.Payload.ReleaseRevision <= 0 ||
            integrationEvent.Payload.AffectedOperations is null ||
            (!isExplicit && !isSuperseded) ||
            (isExplicit && !string.IsNullOrWhiteSpace(integrationEvent.Payload.SupersededByPlanId)) ||
            (isSuperseded && string.IsNullOrWhiteSpace(integrationEvent.Payload.SupersededByPlanId)))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "mes.schedulePlanRevoked.invalidPayload",
                    "Schedule revocation requires a plan id, positive release revision, affected operations, and consistent explicit/superseded successor semantics."),
                cancellationToken);
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var watermark = await dbContext.ScheduleReleaseWatermarks.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId,
            cancellationToken);
        if (watermark is null)
        {
            dbContext.ScheduleReleaseWatermarks.Add(new ScheduleReleaseWatermark(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.Payload.PlanId,
                integrationEvent.Payload.ReleaseRevision,
                integrationEvent.OccurredAtUtc));
        }
        else
        {
            watermark.RecordRevocation(
                integrationEvent.Payload.PlanId,
                integrationEvent.Payload.ReleaseRevision,
                integrationEvent.OccurredAtUtc);
        }

        var operationIds = integrationEvent.Payload.AffectedOperations
            .Select(x => x.OperationId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (operationIds.Length > 0)
        {
            var tasks = await dbContext.OperationTasks
                .Where(x =>
                    x.OrganizationId == integrationEvent.OrganizationId &&
                    x.EnvironmentId == integrationEvent.EnvironmentId &&
                    operationIds.Contains(x.OperationTaskIdValue))
                .ToArrayAsync(cancellationToken);

            foreach (var task in tasks)
            {
                task.RevokeScheduleAssignment(
                    integrationEvent.Payload.PlanId,
                    integrationEvent.Payload.ReleaseRevision,
                    integrationEvent.Payload.Reason);
            }
        }
    }
}

public static class SchedulePlanRevokedIntegrationEventTopic
{
    public const string TopicName = "SchedulePlanRevokedIntegrationEvent";
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
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var tasks = await dbContext.OperationTasks
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                operationIds.Contains(x.OperationTaskIdValue))
            .ToArrayAsync(cancellationToken);

        var reasonCode = string.IsNullOrWhiteSpace(integrationEvent.Payload.ReasonCode)
            ? null
            : integrationEvent.Payload.ReasonCode.Trim();
        foreach (var task in tasks)
        {
            task.MarkScheduleInvalidated(reasonCode);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class SchedulePlanInvalidatedIntegrationEventTopic
{
    public const string TopicName = "SchedulePlanInvalidatedIntegrationEvent";
}
