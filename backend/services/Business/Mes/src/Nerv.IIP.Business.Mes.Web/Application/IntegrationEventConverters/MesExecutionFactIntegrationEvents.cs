using System.Globalization;
using MediatR;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

public sealed class OperationTaskStartedIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
{
    public MesOperationTaskStartedIntegrationEvent Convert(OperationTaskStartedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var context = contextAccessor.GetContext();
        var idempotencyKey = OperationTaskLifecycleIntegrationEventConverter.BuildIdempotencyKey(
            "operation-task-started", task, domainEvent.StartedAtUtc);
        return new MesOperationTaskStartedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.OperationTaskStarted,
            MesIntegrationEventVersions.V1, domainEvent.StartedAtUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, task.OrganizationId, task.EnvironmentId, "system:mes",
            idempotencyKey, OperationTaskLifecycleIntegrationEventConverter.Payload(task, domainEvent.StartedAtUtc));
    }
}

public sealed class OperationTaskPausedIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
{
    public MesOperationTaskPausedIntegrationEvent Convert(OperationTaskPausedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var context = contextAccessor.GetContext();
        var idempotencyKey = OperationTaskLifecycleIntegrationEventConverter.BuildIdempotencyKey(
            "operation-task-paused", task, domainEvent.PausedAtUtc);
        return new MesOperationTaskPausedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.OperationTaskPaused,
            MesIntegrationEventVersions.V1, domainEvent.PausedAtUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, task.OrganizationId, task.EnvironmentId, "system:mes",
            idempotencyKey, OperationTaskLifecycleIntegrationEventConverter.Payload(task, domainEvent.PausedAtUtc));
    }
}

public sealed class OperationTaskResumedIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
{
    public MesOperationTaskResumedIntegrationEvent Convert(OperationTaskResumedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var context = contextAccessor.GetContext();
        var idempotencyKey = OperationTaskLifecycleIntegrationEventConverter.BuildIdempotencyKey(
            "operation-task-resumed", task, domainEvent.ResumedAtUtc);
        return new MesOperationTaskResumedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.OperationTaskResumed,
            MesIntegrationEventVersions.V1, domainEvent.ResumedAtUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, task.OrganizationId, task.EnvironmentId, "system:mes",
            idempotencyKey, OperationTaskLifecycleIntegrationEventConverter.Payload(task, domainEvent.ResumedAtUtc));
    }
}

internal static class OperationTaskLifecycleIntegrationEventConverter
{
    public static string BuildIdempotencyKey(
        string eventName, OperationTask task, DateTimeOffset changedAtUtc) =>
        EventIds.Idempotency(eventName, task.OrganizationId, task.EnvironmentId, task.OperationTaskId,
            changedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));

    public static OperationTaskLifecyclePayload Payload(OperationTask task, DateTimeOffset changedAtUtc) =>
        new(task.WorkOrderId, task.OperationTaskId, task.OperationSequence, task.WorkCenterId, changedAtUtc);
}

public sealed class DowntimeStartedIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
{
    public MesDowntimeStartedIntegrationEvent Convert(DowntimeStartedDomainEvent domainEvent)
    {
        var downtime = domainEvent.Downtime;
        var context = contextAccessor.GetContext();
        var organizationId = downtime.OrganizationId ?? string.Empty;
        var environmentId = downtime.EnvironmentId ?? string.Empty;
        var idempotencyKey = EventIds.Idempotency(
            "downtime-started", organizationId, environmentId, downtime.DowntimeEventNo);
        return new MesDowntimeStartedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.DowntimeStarted,
            MesIntegrationEventVersions.V1, downtime.FromUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, organizationId, environmentId, "system:mes", idempotencyKey,
            new DowntimeStartedPayload(
                downtime.DowntimeEventNo, downtime.WorkOrderId, downtime.OperationTaskId,
                downtime.WorkCenterId, downtime.DeviceAssetId, downtime.Reason, downtime.FromUtc, downtime.ToUtc));
    }
}

public sealed class DowntimeRestoredIntegrationEventConverter(IMesIntegrationEventContextAccessor contextAccessor)
{
    public MesDowntimeRestoredIntegrationEvent Convert(DowntimeRestoredDomainEvent domainEvent)
    {
        var downtime = domainEvent.Downtime;
        var context = contextAccessor.GetContext();
        var organizationId = downtime.OrganizationId ?? string.Empty;
        var environmentId = downtime.EnvironmentId ?? string.Empty;
        var idempotencyKey = EventIds.Idempotency(
            "downtime-restored", organizationId, environmentId, downtime.DowntimeEventNo);
        return new MesDowntimeRestoredIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}", MesIntegrationEventTypes.DowntimeRestored,
            MesIntegrationEventVersions.V1, domainEvent.RestoredAtUtc, MesIntegrationEventSources.BusinessMes,
            context.CorrelationId, context.CausationId, organizationId, environmentId, "system:mes", idempotencyKey,
            new DowntimeRestoredPayload(
                downtime.DowntimeEventNo, downtime.WorkOrderId, downtime.OperationTaskId,
                downtime.WorkCenterId, downtime.DeviceAssetId, downtime.Reason,
                downtime.FromUtc, domainEvent.RestoredAtUtc));
    }
}

internal sealed class MesExecutionFactIntegrationEventPublisher(
    IMesIntegrationEventOutboxPublisher publisher,
    MesActualTimeTopicOptions topicOptions,
    OperationTaskStartedIntegrationEventConverter startedConverter,
    OperationTaskPausedIntegrationEventConverter pausedConverter,
    OperationTaskResumedIntegrationEventConverter resumedConverter,
    DowntimeStartedIntegrationEventConverter downtimeStartedConverter,
    DowntimeRestoredIntegrationEventConverter downtimeRestoredConverter)
    : INotificationHandler<OperationTaskStartedDomainEvent>,
      INotificationHandler<OperationTaskPausedDomainEvent>,
      INotificationHandler<OperationTaskResumedDomainEvent>,
      INotificationHandler<DowntimeStartedDomainEvent>,
      INotificationHandler<DowntimeRestoredDomainEvent>
{
    public Task Handle(OperationTaskStartedDomainEvent notification, CancellationToken cancellationToken) =>
        publisher.PublishAsync(MesExecutionFactIntegrationEventTopics.OperationTaskStarted(topicOptions.DeploymentEnvironment),
            startedConverter.Convert(notification));

    public Task Handle(OperationTaskPausedDomainEvent notification, CancellationToken cancellationToken) =>
        publisher.PublishAsync(MesExecutionFactIntegrationEventTopics.OperationTaskPaused(topicOptions.DeploymentEnvironment),
            pausedConverter.Convert(notification));

    public Task Handle(OperationTaskResumedDomainEvent notification, CancellationToken cancellationToken) =>
        publisher.PublishAsync(MesExecutionFactIntegrationEventTopics.OperationTaskResumed(topicOptions.DeploymentEnvironment),
            resumedConverter.Convert(notification));

    public Task Handle(DowntimeStartedDomainEvent notification, CancellationToken cancellationToken) =>
        publisher.PublishAsync(MesExecutionFactIntegrationEventTopics.DowntimeStarted(topicOptions.DeploymentEnvironment),
            downtimeStartedConverter.Convert(notification));

    public Task Handle(DowntimeRestoredDomainEvent notification, CancellationToken cancellationToken) =>
        publisher.PublishAsync(MesExecutionFactIntegrationEventTopics.DowntimeRestored(topicOptions.DeploymentEnvironment),
            downtimeRestoredConverter.Convert(notification));
}
