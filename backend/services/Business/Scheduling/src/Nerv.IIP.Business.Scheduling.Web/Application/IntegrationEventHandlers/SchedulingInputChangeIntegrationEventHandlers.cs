using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent", ConsumerName)]
public sealed class AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<AssetUnavailableIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.asset-unavailable";

    private readonly IntegrationEventConsumerGuard<AssetUnavailableIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1));

    public async Task HandleAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateByResourceAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            integrationEvent.Payload.DeviceAssetId,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetRestoredIntegrationEvent", ConsumerName)]
public sealed class AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<AssetRestoredIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.asset-restored";

    private readonly IntegrationEventConsumerGuard<AssetRestoredIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MaintenanceIntegrationEventTypes.AssetRestored,
            MaintenanceIntegrationEventVersions.V1));

    public async Task HandleAsync(AssetRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Maintenance.AssetRestoredIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(AssetRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(AssetRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateByResourceAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            SchedulingPlanInvalidationReasons.EquipmentRestored,
            integrationEvent.Payload.DeviceAssetId,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockAvailabilityChangedIntegrationEvent", ConsumerName)]
public sealed class StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<StockAvailabilityChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.stock-availability-changed";

    private readonly IntegrationEventConsumerGuard<StockAvailabilityChangedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            InventoryIntegrationEventTypes.StockAvailabilityChanged,
            InventoryIntegrationEventVersions.V1));

    public async Task HandleAsync(StockAvailabilityChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Inventory.StockAvailabilityChangedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(StockAvailabilityChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(StockAvailabilityChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateAllGeneratedPlansAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            SchedulingPlanInvalidationReasons.MaterialReadinessChanged,
            affectedSkuCode: integrationEvent.Payload.SkuCode,
            cancellationToken: cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.quality-inspection-result";

    private static readonly string[] SupportedEventTypes =
    [
        QualityIntegrationEventTypes.InspectionPassed,
        QualityIntegrationEventTypes.InspectionConditionalReleased,
        QualityIntegrationEventTypes.InspectionRejected,
    ];

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SupportedEventTypes,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.Payload.SourceService, QualityIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var reason = string.Equals(integrationEvent.EventType, QualityIntegrationEventTypes.InspectionRejected, StringComparison.Ordinal)
            ? SchedulingPlanInvalidationReasons.QualityBlocked
            : SchedulingPlanInvalidationReasons.QualityReleased;

        await SchedulingPlanInvalidationService.InvalidateByWorkOrderOrOperationAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            reason,
            integrationEvent.Payload.SourceDocumentId,
            integrationEvent.Payload.SkuCode,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.WorkOrderReleasedIntegrationEvent", ConsumerName)]
public sealed class WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<WorkOrderReleasedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.work-order-released";

    private readonly IntegrationEventConsumerGuard<WorkOrderReleasedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.WorkOrderReleased,
            MesIntegrationEventVersions.V1));

    public async Task HandleAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Mes.WorkOrderReleasedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateAllGeneratedPlansAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            SchedulingPlanInvalidationReasons.WorkOrderReleased,
            affectedWorkOrderId: integrationEvent.Payload.WorkOrderId,
            affectedSkuCode: integrationEvent.Payload.SkuCode,
            cancellationToken: cancellationToken);
    }
}

internal static class SchedulingPlanInvalidationService
{
    public static async Task InvalidateByResourceAsync<TIntegrationEvent>(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string affectedResourceId,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        var planIds = await dbContext.SchedulePlans
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.Status == SchedulePlanLifecycleStatus.Generated &&
                x.Assignments.Any(assignment =>
                    assignment.ResourceId == affectedResourceId ||
                    assignment.WorkCenterId == affectedResourceId))
            .Select(x => x.PlanId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        await AddInvalidationsAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            reasonCode,
            planIds,
            affectedResourceId: affectedResourceId,
            affectedWorkOrderId: null,
            affectedOperationId: null,
            affectedSkuCode: null,
            cancellationToken);
    }

    public static async Task InvalidateByWorkOrderOrOperationAsync<TIntegrationEvent>(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string sourceDocumentId,
        string? affectedSkuCode,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        var normalizedSource = Required(sourceDocumentId, nameof(sourceDocumentId));
        var planIds = await dbContext.SchedulePlans
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.Status == SchedulePlanLifecycleStatus.Generated &&
                x.Assignments.Any(assignment =>
                    assignment.WorkOrderId == normalizedSource ||
                    assignment.OperationId == normalizedSource))
            .Select(x => x.PlanId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        await AddInvalidationsAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            reasonCode,
            planIds,
            affectedResourceId: null,
            affectedWorkOrderId: normalizedSource,
            affectedOperationId: null,
            affectedSkuCode: affectedSkuCode,
            cancellationToken);
    }

    public static async Task InvalidateAllGeneratedPlansAsync<TIntegrationEvent>(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string? affectedWorkOrderId = null,
        string? affectedSkuCode = null,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        var planIds = await dbContext.SchedulePlans
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.Status == SchedulePlanLifecycleStatus.Generated)
            .Select(x => x.PlanId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        await AddInvalidationsAsync(
            dbContext,
            timeProvider,
            integrationEvent,
            reasonCode,
            planIds,
            affectedResourceId: null,
            affectedWorkOrderId: affectedWorkOrderId,
            affectedOperationId: null,
            affectedSkuCode: affectedSkuCode,
            cancellationToken);
    }

    private static async Task AddInvalidationsAsync<TIntegrationEvent>(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        IReadOnlyCollection<string> planIds,
        string? affectedResourceId,
        string? affectedWorkOrderId,
        string? affectedOperationId,
        string? affectedSkuCode,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        if (planIds.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var existingPlanIds = await dbContext.SchedulePlanInvalidations
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SourceEventType == integrationEvent.EventType &&
                x.SourceEventId == integrationEvent.EventId)
            .Select(x => x.PlanId)
            .ToArrayAsync(cancellationToken);
        var existing = existingPlanIds.ToHashSet(StringComparer.Ordinal);
        var recordedAtUtc = timeProvider.GetUtcNow();

        foreach (var planId in planIds.Where(x => !existing.Contains(x)).Order(StringComparer.Ordinal))
        {
            dbContext.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                planId,
                integrationEvent.EventId,
                integrationEvent.EventType,
                integrationEvent.SourceService,
                reasonCode,
                affectedResourceId,
                affectedWorkOrderId,
                affectedOperationId,
                affectedSkuCode,
                integrationEvent.OccurredAtUtc,
                recordedAtUtc));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}

internal static class SchedulingProcessedIntegrationEventInbox
{
    public static Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return ProcessedIntegrationEventInbox.TryRecordAsync(
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
            cancellationToken);
    }
}
