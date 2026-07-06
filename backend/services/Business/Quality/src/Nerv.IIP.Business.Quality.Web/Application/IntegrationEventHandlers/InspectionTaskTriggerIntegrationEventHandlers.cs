using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.wms-inbound-completed-inspection-tasks";

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, WmsIntegrationEventTypes.InboundOrderCompleted, WmsIntegrationEventVersions.V1));

    public async Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.EventType != WmsIntegrationEventTypes.InboundOrderCompleted)
        {
            return;
        }

        var payload = integrationEvent.Payload;
        var lines = payload.Lines?.Count > 0
            ? payload.Lines
            : payload.SkuCode is null || payload.UomCode is null || payload.Quantity is null
                ? []
                : [new WmsIntegrationPayloadLine(payload.LineReference ?? payload.PublicReference, payload.SkuCode, payload.UomCode, payload.SiteCode, payload.LocationCode, payload.Quantity.Value, payload.Status)];
        foreach (var line in lines)
        {
            if (InspectionTaskGeneration.ShouldSkipInspection(line.Status))
            {
                continue;
            }

            await InspectionTaskGeneration.TryAddTaskAsync(
                dbContext,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                sourceType: "receiving",
                sourceService: "wms",
                sourceDocumentId: payload.PublicReference,
                sourceDocumentLineId: line.LineReference,
                skuCode: line.SkuCode,
                quantity: line.Quantity,
                uomCode: line.UomCode,
                batchNo: null,
                serialNo: null,
                workCenterId: null,
                sourceDocumentType: payload.SourceDocumentType,
                occurredAtUtc: integrationEvent.OccurredAtUtc,
                triggerIdempotencyKey: $"{integrationEvent.IdempotencyKey}:{line.LineReference}",
                cancellationToken);
        }

        await InspectionTaskGeneration.SaveChangesIgnoreDuplicateTasksAsync(dbContext, cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.PurchaseReceiptRecordedIntegrationEvent", ConsumerName)]
public sealed class ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<PurchaseReceiptRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.erp-purchase-receipt-recorded-inspection-tasks";

    private readonly IntegrationEventConsumerGuard<PurchaseReceiptRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, ErpIntegrationEventTypes.PurchaseReceiptRecorded, ErpIntegrationEventVersions.V1));

    public async Task HandleAsync(PurchaseReceiptRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Erp.PurchaseReceiptRecordedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(PurchaseReceiptRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(PurchaseReceiptRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        foreach (var line in payload.Lines ?? [])
        {
            if (InspectionTaskGeneration.ShouldSkipInspection(line.QualityStatus))
            {
                continue;
            }

            await InspectionTaskGeneration.TryAddTaskAsync(
                dbContext,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                sourceType: "receiving",
                sourceService: "erp",
                sourceDocumentId: payload.PurchaseReceiptNo,
                sourceDocumentLineId: line.LineReference,
                skuCode: line.SkuCode,
                quantity: line.ReceivedQuantity,
                uomCode: line.UomCode,
                batchNo: line.LotNo,
                serialNo: null,
                workCenterId: null,
                sourceDocumentType: "purchase-receipt",
                occurredAtUtc: integrationEvent.OccurredAtUtc,
                triggerIdempotencyKey: $"{integrationEvent.IdempotencyKey}:{line.LineReference}",
                cancellationToken);
        }

        await InspectionTaskGeneration.SaveChangesIgnoreDuplicateTasksAsync(dbContext, cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.OperationTaskCompletedIntegrationEvent", ConsumerName)]
public sealed class MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-operation-completed-inspection-tasks";

    private readonly IntegrationEventConsumerGuard<OperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.OperationTaskCompleted, MesIntegrationEventVersions.V1));

    public async Task HandleAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Mes.OperationTaskCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!payload.RequiresQualityInspection)
        {
            return;
        }

        await InspectionTaskGeneration.TryAddTaskAsync(
            dbContext,
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            sourceType: "operation",
            sourceService: "mes",
            sourceDocumentId: payload.WorkOrderId,
            sourceDocumentLineId: payload.OperationTaskId,
            skuCode: payload.SkuCode,
            quantity: payload.PlannedQuantity,
            uomCode: payload.UomCode,
            batchNo: null,
            serialNo: null,
            workCenterId: payload.WorkCenterId,
            sourceDocumentType: "operation-task",
            occurredAtUtc: integrationEvent.OccurredAtUtc,
            triggerIdempotencyKey: integrationEvent.IdempotencyKey,
            cancellationToken);
        await InspectionTaskGeneration.SaveChangesIgnoreDuplicateTasksAsync(dbContext, cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.FinishedGoodsReceiptRequestedIntegrationEvent", ConsumerName)]
public sealed class MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<FinishedGoodsReceiptRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-finished-goods-receipt-inspection-tasks";

    private readonly IntegrationEventConsumerGuard<FinishedGoodsReceiptRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.FinishedGoodsReceiptRequested, MesIntegrationEventVersions.V1));

    public async Task HandleAsync(FinishedGoodsReceiptRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Mes.FinishedGoodsReceiptRequestedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(FinishedGoodsReceiptRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(FinishedGoodsReceiptRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        await InspectionTaskGeneration.TryAddTaskAsync(
            dbContext,
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            sourceType: "final",
            sourceService: "mes",
            sourceDocumentId: payload.RequestNo,
            sourceDocumentLineId: payload.WorkOrderId,
            skuCode: payload.SkuCode,
            quantity: payload.Quantity,
            uomCode: payload.UomCode,
            batchNo: payload.ProducedLotNo,
            serialNo: payload.SerialNo,
            workCenterId: null,
            sourceDocumentType: "finished-goods-receipt",
            occurredAtUtc: integrationEvent.OccurredAtUtc,
            triggerIdempotencyKey: integrationEvent.IdempotencyKey,
            cancellationToken);
        await InspectionTaskGeneration.SaveChangesIgnoreDuplicateTasksAsync(dbContext, cancellationToken);
    }
}

internal static class InspectionTaskGeneration
{
    public static bool ShouldSkipInspection(string? qualityStatus)
    {
        return WmsReceivingQualityStatuses.ShouldSkipInspection(qualityStatus);
    }

    public static async Task TryAddTaskAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string skuCode,
        decimal quantity,
        string uomCode,
        string? batchNo,
        string? serialNo,
        string? workCenterId,
        string? sourceDocumentType,
        DateTimeOffset occurredAtUtc,
        string triggerIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        var normalizedSourceService = sourceService.Trim().ToLowerInvariant();
        var normalizedSourceDocumentId = sourceDocumentId.Trim();
        var normalizedSourceDocumentLineId = string.IsNullOrWhiteSpace(sourceDocumentLineId) ? null : sourceDocumentLineId.Trim();
        var normalizedSkuCode = skuCode.Trim();
        var normalizedTriggerIdempotencyKey = triggerIdempotencyKey.Trim();

        if (quantity <= 0m
            || dbContext.InspectionTasks.Local.Any(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.TriggerIdempotencyKey == normalizedTriggerIdempotencyKey)
            || dbContext.InspectionTasks.Local.Any(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.SourceType == normalizedSourceType &&
                x.SourceService == normalizedSourceService &&
                x.SourceDocumentId == normalizedSourceDocumentId &&
                x.SourceDocumentLineId == normalizedSourceDocumentLineId &&
                x.SkuCode == normalizedSkuCode)
            ||
            await dbContext.InspectionTasks.AnyAsync(
                x => x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.TriggerIdempotencyKey == normalizedTriggerIdempotencyKey,
                cancellationToken)
            ||
            await dbContext.InspectionTasks.AnyAsync(
                x => x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.SourceType == normalizedSourceType &&
                    x.SourceService == normalizedSourceService &&
                    x.SourceDocumentId == normalizedSourceDocumentId &&
                    x.SourceDocumentLineId == normalizedSourceDocumentLineId &&
                    x.SkuCode == normalizedSkuCode,
                cancellationToken))
        {
            return;
        }

        var plan = await MatchPlanAsync(
            dbContext,
            organizationId,
            environmentId,
            sourceType,
            skuCode,
            workCenterId,
            sourceDocumentType,
            cancellationToken);
        if (plan is null)
        {
            return;
        }

        dbContext.InspectionTasks.Add(InspectionTask.CreatePending(
            organizationId,
            environmentId,
            plan.Id,
            normalizedSourceType,
            normalizedSourceService,
            normalizedSourceDocumentId,
            normalizedSourceDocumentLineId,
            normalizedSkuCode,
            quantity,
            uomCode,
            batchNo,
            serialNo,
            occurredAtUtc,
            occurredAtUtc.AddHours(24),
            normalizedTriggerIdempotencyKey));
    }

    public static async Task SaveChangesIgnoreDuplicateTasksAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!dbContext.ChangeTracker.HasChanges())
        {
            return;
        }

        var pendingTasks = dbContext.ChangeTracker.Entries<InspectionTask>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .ToArray();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateInspectionTaskConflict(ex))
        {
            dbContext.ChangeTracker.Clear();
            await SavePendingInspectionTasksIndividuallyAsync(dbContext, pendingTasks, cancellationToken);
        }
    }

    private static async Task SavePendingInspectionTasksIndividuallyAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<InspectionTask> pendingTasks,
        CancellationToken cancellationToken)
    {
        foreach (var task in pendingTasks)
        {
            if (await TaskAlreadyExistsAsync(dbContext, task, cancellationToken))
            {
                continue;
            }

            dbContext.InspectionTasks.Add(task);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsDuplicateInspectionTaskConflict(ex))
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private static Task<bool> TaskAlreadyExistsAsync(
        ApplicationDbContext dbContext,
        InspectionTask task,
        CancellationToken cancellationToken)
    {
        return dbContext.InspectionTasks.AnyAsync(
            x =>
                x.OrganizationId == task.OrganizationId &&
                x.EnvironmentId == task.EnvironmentId &&
                (x.TriggerIdempotencyKey == task.TriggerIdempotencyKey ||
                    (x.SourceType == task.SourceType &&
                     x.SourceService == task.SourceService &&
                     x.SourceDocumentId == task.SourceDocumentId &&
                     x.SourceDocumentLineId == task.SourceDocumentLineId &&
                     x.SkuCode == task.SkuCode)),
            cancellationToken);
    }

    private static bool IsDuplicateInspectionTaskConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("ux_inspection_tasks_scope_trigger_key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ux_inspection_tasks_scope_source_sku", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<InspectionPlan?> MatchPlanAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string sourceType,
        string skuCode,
        string? workCenterId,
        string? sourceDocumentType,
        CancellationToken cancellationToken)
    {
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        var query = dbContext.InspectionPlans
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Status == "active" &&
                x.Category == normalizedSourceType &&
                (x.SkuCode == null || x.SkuCode == skuCode));
        if (!string.IsNullOrWhiteSpace(workCenterId))
        {
            query = query.Where(x => x.WorkCenterId == null || x.WorkCenterId == workCenterId);
        }

        if (!string.IsNullOrWhiteSpace(sourceDocumentType))
        {
            query = query.Where(x => x.DocumentType == null || x.DocumentType == sourceDocumentType);
        }

        return query
            .OrderByDescending(x => x.SkuCode != null)
            .ThenByDescending(x => x.WorkCenterId != null)
            .ThenByDescending(x => x.DocumentType != null)
            .ThenByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
