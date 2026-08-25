using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(nameof(WorkOrderReleasedIntegrationEvent), ConsumerName)]
public sealed class WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WorkOrderReleasedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-work-order-released-periodic-inspection";

    private readonly IntegrationEventConsumerGuard<WorkOrderReleasedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.WorkOrderReleased,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(WorkOrderReleasedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        WorkOrderReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = integrationEvent.Payload;
            var operations = ValidateReleasedOperations(payload);
            var workCenterIds = operations.Select(x => x.WorkCenterId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
            var plans = await dbContext.InspectionPlans
                .AsNoTracking()
                .Where(plan =>
                    plan.OrganizationId == integrationEvent.OrganizationId
                    && plan.EnvironmentId == integrationEvent.EnvironmentId
                    && plan.Status == "active"
                    && plan.Category == "operation"
                    && plan.SkuCode == payload.SkuCode.Trim()
                    && plan.WorkCenterId != null
                    && workCenterIds.Contains(plan.WorkCenterId)
                    && (plan.TimeIntervalHours != null || plan.QuantityInterval != null))
                .ToArrayAsync(cancellationToken);

            await scopeCoordinator.ExecuteAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                operations.Select(x => x.OperationId).ToArray(),
                async ct =>
                {
                    if (!await QualityProcessedIntegrationEventInbox.TryRecordAsync(
                            dbContext,
                            ConsumerName,
                            integrationEvent,
                            ct))
                    {
                        return;
                    }

                    foreach (var operationPayload in operations)
                    {
                        var operation = await PeriodicInspectionOperationEventProcessing.LoadOrCreateAsync(
                            dbContext,
                            integrationEvent.OrganizationId,
                            integrationEvent.EnvironmentId,
                            payload.WorkOrderId,
                            operationPayload.OperationId,
                            ct);
                        var snapshots = plans
                            .Where(plan => plan.WorkCenterId == operationPayload.WorkCenterId.Trim())
                            .OrderBy(plan => plan.PlanCode, StringComparer.Ordinal)
                            .Select(PeriodicInspectionPlanSnapshot.From)
                            .ToArray();
                        operation.ApplyRelease(
                            payload.SkuCode,
                            operationPayload.OperationSequence,
                            operationPayload.WorkCenterId,
                            payload.ReleasedAtUtc.UtcDateTime,
                            snapshots);
                        PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                            dbContext,
                            operation.RuntimeContexts,
                            integrationEvent.OccurredAtUtc);
                    }
                },
                cancellationToken);
        }
        catch (Exception exception) when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
        {
            await PeriodicInspectionOperationEventProcessing.DeadLetterAsync(
                dbContext,
                deadLetterStore,
                ConsumerName,
                integrationEvent,
                exception,
                cancellationToken);
        }
    }

    private static ReleasedOperationPayload[] ValidateReleasedOperations(WorkOrderReleasedPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.WorkOrderId)
            || string.IsNullOrWhiteSpace(payload.SkuCode)
            || payload.ReleasedAtUtc == default
            || payload.Operations is null
            || payload.Operations.Count == 0)
        {
            throw new ArgumentException("Work-order release payload requires work order, SKU, release time and operations.");
        }

        var operations = payload.Operations.ToArray();
        if (operations.Any(operation =>
                string.IsNullOrWhiteSpace(operation.OperationId)
                || operation.OperationSequence <= 0
                || string.IsNullOrWhiteSpace(operation.WorkCenterId)))
        {
            throw new ArgumentException("Released operations require operation id, positive sequence and work center.");
        }

        var duplicate = operations
            .GroupBy(operation => operation.OperationId.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Work-order release contains duplicate operation '{duplicate.Key}'.");
        }

        return operations.OrderBy(operation => operation.OperationId, StringComparer.Ordinal).ToArray();
    }
}

[IntegrationEventConsumer(nameof(ProductionReportRecordedIntegrationEvent), ConsumerName)]
public sealed class ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ProductionReportRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-production-report-periodic-inspection";

    private readonly IntegrationEventConsumerGuard<ProductionReportRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(ProductionReportRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        ProductionReportRecordedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = integrationEvent.Payload;
            if (string.IsNullOrWhiteSpace(payload.WorkOrderId)
                || string.IsNullOrWhiteSpace(payload.OperationTaskId)
                || string.IsNullOrWhiteSpace(payload.ReportNo)
                || string.IsNullOrWhiteSpace(payload.WorkCenterId)
                || string.IsNullOrWhiteSpace(payload.UomCode)
                || payload.ReportedAtUtc == default)
            {
                throw new ArgumentException("Production report payload requires report, work order, operation, work center, UOM and report time.");
            }

            await scopeCoordinator.ExecuteAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                [payload.OperationTaskId],
                async ct =>
                {
                    if (!await QualityProcessedIntegrationEventInbox.TryRecordAsync(
                            dbContext,
                            ConsumerName,
                            integrationEvent,
                            ct))
                    {
                        return;
                    }

                    var operation = await PeriodicInspectionOperationEventProcessing.LoadOrCreateAsync(
                        dbContext,
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        payload.WorkOrderId,
                        payload.OperationTaskId,
                        ct);
                    operation.RecordProductionReport(
                        payload.ReportNo,
                        payload.WorkCenterId,
                        payload.GoodQuantity,
                        payload.UomCode,
                        payload.ReportedAtUtc.UtcDateTime,
                        payload.IsReversal,
                        payload.ReversedReportNo);
                    PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                        dbContext,
                        operation.RuntimeContexts,
                        integrationEvent.OccurredAtUtc);
                },
                cancellationToken);
        }
        catch (Exception exception) when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
        {
            await PeriodicInspectionOperationEventProcessing.DeadLetterAsync(
                dbContext,
                deadLetterStore,
                ConsumerName,
                integrationEvent,
                exception,
                cancellationToken);
        }
    }
}

[IntegrationEventConsumer(nameof(MesOperationTaskCompletedIntegrationEvent), ConsumerName)]
public sealed class MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<MesOperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-operation-completed-periodic-inspection";

    private readonly IntegrationEventConsumerGuard<MesOperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.OperationTaskCompleted,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskCompletedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        MesOperationTaskCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = integrationEvent.Payload;
            if (string.IsNullOrWhiteSpace(payload.WorkOrderId)
                || string.IsNullOrWhiteSpace(payload.OperationTaskId)
                || string.IsNullOrWhiteSpace(payload.SkuCode)
                || payload.OperationSequence <= 0
                || string.IsNullOrWhiteSpace(payload.WorkCenterId)
                || string.IsNullOrWhiteSpace(payload.UomCode)
                || payload.CompletedAtUtc == default)
            {
                throw new ArgumentException("Operation completion payload requires work order, operation, SKU, sequence, work center, UOM and completion time.");
            }

            await scopeCoordinator.ExecuteAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                [payload.OperationTaskId],
                async ct =>
                {
                    if (!await QualityProcessedIntegrationEventInbox.TryRecordAsync(
                            dbContext,
                            ConsumerName,
                            integrationEvent,
                            ct))
                    {
                        return;
                    }

                    var operation = await PeriodicInspectionOperationEventProcessing.LoadOrCreateAsync(
                        dbContext,
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        payload.WorkOrderId,
                        payload.OperationTaskId,
                        ct);
                    operation.Complete(
                        payload.SkuCode,
                        payload.OperationSequence,
                        payload.WorkCenterId,
                        payload.UomCode,
                        payload.CompletedAtUtc.UtcDateTime);
                },
                cancellationToken);
        }
        catch (Exception exception) when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
        {
            await PeriodicInspectionOperationEventProcessing.DeadLetterAsync(
                dbContext,
                deadLetterStore,
                ConsumerName,
                integrationEvent,
                exception,
                cancellationToken);
        }
    }
}

internal static class PeriodicInspectionQuantityTaskGeneration
{
    public const int MaxWindowsPerTransaction = 256;

    public static void AddDueTasks(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<PeriodicInspectionRuntimeContext> contexts,
        DateTimeOffset occurredAtUtc,
        int maxWindows = MaxWindowsPerTransaction,
        DateTime? continuationNextAttemptAtUtc = null)
    {
        foreach (var context in contexts.OrderBy(x => x.Id))
        {
            foreach (var window in context.TakeDueQuantityWindows(
                         occurredAtUtc.UtcDateTime,
                         maxWindows,
                         continuationNextAttemptAtUtc))
            {
                var generatedAtUtc = new DateTimeOffset(window.GeneratedAtUtc);
                var task = InspectionTask.CreatePending(
                    context.OrganizationId,
                    context.EnvironmentId,
                    context.InspectionPlanId,
                    sourceType: "operation",
                    sourceService: "mes",
                    sourceDocumentId: context.WorkOrderId,
                    sourceDocumentLineId: $"{context.OperationId}:periodic-quantity:{context.Id.Id:D}:{window.Sequence}",
                    skuCode: context.SkuCode,
                    quantity: window.ThresholdQuantity,
                    uomCode: context.UomCode!,
                    batchNo: null,
                    serialNo: null,
                    generatedAtUtc,
                    dueAtUtc: generatedAtUtc.AddHours(24),
                    triggerIdempotencyKey: $"quality:periodic-quantity:{context.Id.Id:D}:{window.Sequence}");
                if (context.AssignedInspectorUserId is not null || context.AssignedTeamId is not null)
                {
                    task.Assign(
                        context.AssignedInspectorUserId,
                        context.AssignedTeamId,
                        task.Version,
                        generatedAtUtc);
                }

                dbContext.InspectionTasks.Add(task);
            }
        }
    }
}

internal static class QualityProcessedIntegrationEventInbox
{
    public static async Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            consumerName,
            integrationEvent,
            record => new ProcessedIntegrationEvent(
                record.ConsumerName,
                record.EventId,
                record.EventType,
                record.EventVersion,
                record.SourceService,
                record.IdempotencyKey,
                record.ProcessedAtUtc),
            ProcessedIntegrationEventInboxIdentity.EventId,
            AcquireEventIdentityLockAsync,
            cancellationToken);
    }

    private static async Task AcquireEventIdentityLockAsync(
        DbContext dbContext,
        string consumerName,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        var lockKey = $"quality-integration-event-inbox:{consumerName}:{eventId}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}

internal static class PeriodicInspectionOperationEventProcessing
{
    public static async Task<PeriodicInspectionOperation> LoadOrCreateAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkOrderId = Required(workOrderId);
        var normalizedOperationId = Required(operationId);
        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.WorkOrderId == normalizedWorkOrderId
                    && x.OperationId == normalizedOperationId,
                cancellationToken);
        if (operation is not null)
        {
            return operation;
        }

        operation = PeriodicInspectionOperation.CreatePending(
            organizationId,
            environmentId,
            normalizedWorkOrderId,
            normalizedOperationId);
        dbContext.PeriodicInspectionOperations.Add(operation);
        return operation;
    }

    public static bool IsInvalidBusinessFact(Exception exception) =>
        exception is ArgumentException or InvalidOperationException;

    public static async Task DeadLetterAsync<TIntegrationEvent>(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        string consumerName,
        TIntegrationEvent integrationEvent,
        Exception exception,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        dbContext.ChangeTracker.Clear();
        await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "invalid-business-facts",
                exception.Message),
            cancellationToken);
    }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("MES business identity is required.")
            : value.Trim();
}
