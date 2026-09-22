using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationTaskStartedIntegrationEvent", ConsumerName)]
public sealed class MesOperationTaskStartedIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<MesOperationTaskStartedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-operation-started";
    private readonly IntegrationEventConsumerGuard<MesOperationTaskStartedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.OperationTaskStarted, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskStartedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskStartedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskStartedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(MesOperationTaskStartedIntegrationEvent value, CancellationToken cancellationToken) =>
        OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, value.Payload.OperationSequence,
            value.Payload.WorkCenterId,
            projection => projection.ApplyStarted(value.Payload.ChangedAtUtc, value.EventId),
            cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationTaskPausedIntegrationEvent", ConsumerName)]
public sealed class MesOperationTaskPausedIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<MesOperationTaskPausedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-operation-paused";
    private readonly IntegrationEventConsumerGuard<MesOperationTaskPausedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.OperationTaskPaused, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskPausedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskPausedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskPausedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(MesOperationTaskPausedIntegrationEvent value, CancellationToken cancellationToken) =>
        OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, value.Payload.OperationSequence,
            value.Payload.WorkCenterId,
            projection => projection.ApplyPaused(value.Payload.ChangedAtUtc, value.EventId),
            cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationTaskResumedIntegrationEvent", ConsumerName)]
public sealed class MesOperationTaskResumedIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<MesOperationTaskResumedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-operation-resumed";
    private readonly IntegrationEventConsumerGuard<MesOperationTaskResumedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.OperationTaskResumed, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskResumedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskResumedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskResumedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(MesOperationTaskResumedIntegrationEvent value, CancellationToken cancellationToken) =>
        OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, value.Payload.OperationSequence,
            value.Payload.WorkCenterId,
            projection => projection.ApplyResumed(value.Payload.ChangedAtUtc, value.EventId),
            cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationTaskCompletedIntegrationEvent", ConsumerName)]
public sealed class MesOperationTaskCompletedIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<MesOperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-operation-completed";
    private readonly IntegrationEventConsumerGuard<MesOperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.OperationTaskCompleted, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskCompletedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(MesOperationTaskCompletedIntegrationEvent value, CancellationToken cancellationToken) =>
        OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, value.Payload.OperationSequence,
            value.Payload.WorkCenterId,
            projection => projection.ApplyCompleted(value.Payload.CompletedAtUtc, value.EventId),
            cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.ProductionReportRecordedIntegrationEvent", ConsumerName)]
public sealed class ProductionReportRecordedIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<ProductionReportRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-production-report";
    private readonly IntegrationEventConsumerGuard<ProductionReportRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.ProductionReportRecorded, MesIntegrationEventVersions.V1));

    public Task HandleAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(ProductionReportRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(ProductionReportRecordedIntegrationEvent value, CancellationToken cancellationToken) =>
        OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, null, value.Payload.WorkCenterId,
            projection => projection.AddCompletedQuantity(value.Payload.GoodQuantity, value.Payload.ReportedAtUtc, value.EventId),
            cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesDowntimeStartedIntegrationEvent", ConsumerName)]
public sealed class MesDowntimeStartedIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<MesDowntimeStartedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-downtime-started";
    private readonly IntegrationEventConsumerGuard<MesDowntimeStartedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.DowntimeStarted, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesDowntimeStartedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(MesDowntimeStartedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesDowntimeStartedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(MesDowntimeStartedIntegrationEvent value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value.Payload.WorkOrderId) || string.IsNullOrWhiteSpace(value.Payload.OperationTaskId))
        {
            return Task.CompletedTask;
        }

        return OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, null, value.Payload.WorkCenterId,
            projection => projection.ApplyDowntimeStarted(value.Payload.StartedAtUtc, value.EventId),
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesDowntimeRestoredIntegrationEvent", ConsumerName)]
public sealed class MesDowntimeRestoredIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<MesDowntimeRestoredIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-downtime-restored";
    private readonly IntegrationEventConsumerGuard<MesDowntimeRestoredIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.DowntimeRestored, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesDowntimeRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(MesDowntimeRestoredIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesDowntimeRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(MesDowntimeRestoredIntegrationEvent value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value.Payload.WorkOrderId) || string.IsNullOrWhiteSpace(value.Payload.OperationTaskId))
        {
            return Task.CompletedTask;
        }

        return OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, null, value.Payload.WorkCenterId,
            projection => projection.ApplyDowntimeRestored(value.Payload.RestoredAtUtc, value.EventId),
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForProjectExecution(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOperationExecutionProjectionMutationLock mutationLock)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.execution-quality-result";
    private static readonly HashSet<string> AcceptedSourceServices = new(StringComparer.OrdinalIgnoreCase)
    {
        QualityInspectionSourceServices.Mes,
        QualityInspectionSourceServices.MesOperation,
        QualityIntegrationEventSources.BusinessMes,
    };
    private static readonly HashSet<string> AcceptedSourceTypes = new(StringComparer.Ordinal)
    {
        QualityInspectionSourceTypes.Operation,
        QualityInspectionSourceTypes.FirstArticle,
    };
    private static readonly string[] SupportedEventTypes =
    [
        QualityIntegrationEventTypes.InspectionPassed,
        QualityIntegrationEventTypes.InspectionConditionalReleased,
        QualityIntegrationEventTypes.InspectionRejected,
    ];
    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, SupportedEventTypes, QualityIntegrationEventVersions.V1));

    public Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, ProjectAsync, cancellationToken);

    [CapSubscribe(nameof(InspectionResultIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task ProjectAsync(InspectionResultIntegrationEvent value, CancellationToken cancellationToken)
    {
        if (!AcceptedSourceServices.Contains(value.Payload.SourceService)
            || !AcceptedSourceTypes.Contains(value.Payload.SourceType)
            || string.IsNullOrWhiteSpace(value.Payload.WorkOrderId)
            || string.IsNullOrWhiteSpace(value.Payload.OperationTaskId))
        {
            return Task.CompletedTask;
        }

        return OperationExecutionProjectionConsumerPersistence.ProjectAsync(
            dbContext, mutationLock, ConsumerName, value,
            value.Payload.WorkOrderId, value.Payload.OperationTaskId, null, null,
            projection =>
            {
                if (string.Equals(value.EventType, QualityIntegrationEventTypes.InspectionRejected, StringComparison.Ordinal))
                {
                    projection.ApplyQualityBlocked(value.Payload.RecordedAtUtc, value.EventId);
                }
                else
                {
                    projection.ApplyQualityReleased(value.Payload.RecordedAtUtc, value.EventId);
                }
            },
            cancellationToken);
    }
}

internal static class OperationExecutionProjectionConsumerPersistence
{
    public static async Task ProjectAsync(
        ApplicationDbContext dbContext,
        IOperationExecutionProjectionMutationLock mutationLock,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        string workOrderId,
        string operationId,
        int? operationSequence,
        string? workCenterId,
        Action<OperationExecutionProjection> apply,
        CancellationToken cancellationToken)
    {
        await mutationLock.AcquireAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            workOrderId,
            operationId,
            cancellationToken);

        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext, consumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var projection = await dbContext.OperationExecutionProjections.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                 && x.EnvironmentId == integrationEvent.EnvironmentId
                 && x.WorkOrderId == workOrderId
                 && x.OperationId == operationId,
            cancellationToken);
        if (projection is null)
        {
            projection = OperationExecutionProjection.Create(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                workOrderId,
                operationId,
                operationSequence,
                workCenterId,
                integrationEvent.OccurredAtUtc,
                integrationEvent.EventId);
            dbContext.OperationExecutionProjections.Add(projection);
        }
        else
        {
            projection.EnrichIdentity(operationSequence, workCenterId);
        }

        apply(projection);
        await SchedulingProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync(dbContext, cancellationToken);
    }
}
