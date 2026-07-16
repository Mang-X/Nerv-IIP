using DotNetCore.CAP;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

internal static class SchedulingMasterDataResourceTypes
{
    public const string WorkCenter = "WorkCenter";
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.MasterData.WorkCalendarChangedIntegrationEvent", ConsumerName)]
public sealed class WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender,
    ILogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans> logger)
    : IIntegrationEventHandler<WorkCalendarChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.work-calendar-changed";

    private readonly IntegrationEventConsumerGuard<WorkCalendarChangedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MasterDataIntegrationEventTypes.WorkCalendarChanged,
            MasterDataIntegrationEventVersions.V1));

    public Task HandleAsync(WorkCalendarChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(WorkCalendarChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkCalendarChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(WorkCalendarChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordByEventIdAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.Code))
        {
            logger.LogInformation(
                "Scheduling input change {EventType} has no traceable calendar code in {OrganizationId}/{EnvironmentId}; no schedule plan was invalidated.",
                integrationEvent.EventType,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
            await SchedulingProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync(dbContext, cancellationToken);
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateGeneratedPlansByCalendarAsync(
            sender,
            integrationEvent,
            SchedulingPlanInvalidationReasons.WorkCalendarChanged,
            integrationEvent.Payload.Code,
            logger,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.MasterData.ResourceChangedIntegrationEvent", ConsumerName)]
public sealed class ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender,
    ILogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans> logger)
    : IIntegrationEventHandler<ResourceChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.resource-changed";

    private readonly IntegrationEventConsumerGuard<ResourceChangedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MasterDataIntegrationEventTypes.ResourceChanged,
            MasterDataIntegrationEventVersions.V1));

    public Task HandleAsync(ResourceChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ResourceChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ResourceChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(ResourceChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordByEventIdAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.Code) ||
            !string.Equals(
                integrationEvent.Payload.ResourceType,
                SchedulingMasterDataResourceTypes.WorkCenter,
                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Scheduling input change {EventType} for {ResourceType} scope {ScopeValue} matched no schedule plan in {OrganizationId}/{EnvironmentId} because that hierarchy is not traceable from persisted assignments.",
                integrationEvent.EventType,
                integrationEvent.Payload.ResourceType,
                integrationEvent.Payload.Code,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
            await SchedulingProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync(dbContext, cancellationToken);
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateGeneratedPlansByWorkCenterAsync(
            sender,
            integrationEvent,
            SchedulingPlanInvalidationReasons.ResourceChanged,
            integrationEvent.Payload.Code,
            logger,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent", ConsumerName)]
public sealed class AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender,
    ILogger<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans> logger)
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

    [CapSubscribe(nameof(AssetUnavailableIntegrationEvent), Group = ConsumerName)]
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
            sender,
            integrationEvent,
            SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            integrationEvent.Payload.DeviceAssetId,
            logger,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetRestoredIntegrationEvent", ConsumerName)]
public sealed class AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender,
    ILogger<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans> logger)
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

    [CapSubscribe(nameof(AssetRestoredIntegrationEvent), Group = ConsumerName)]
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
            sender,
            integrationEvent,
            SchedulingPlanInvalidationReasons.EquipmentRestored,
            integrationEvent.Payload.DeviceAssetId,
            logger,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.DeviceStateChangedIntegrationEvent", ConsumerName)]
public sealed class DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender,
    ILogger<DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans> logger)
    : IIntegrationEventHandler<DeviceStateChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.device-state-changed";

    private readonly IntegrationEventConsumerGuard<DeviceStateChangedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged,
            IndustrialTelemetryIntegrationEventVersions.V1));

    public async Task HandleAsync(DeviceStateChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(DeviceStateChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(DeviceStateChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(DeviceStateChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await SchedulingPlanInvalidationService.InvalidateByResourceAsync(
            sender,
            integrationEvent,
            SchedulingPlanInvalidationReasons.DeviceStateChanged,
            integrationEvent.Payload.DeviceAssetId,
            logger,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockAvailabilityChangedIntegrationEvent", ConsumerName)]
public sealed class StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender)
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

    [CapSubscribe(nameof(StockAvailabilityChangedIntegrationEvent), Group = ConsumerName)]
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
            sender,
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
    ISender sender)
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

    [CapSubscribe(nameof(InspectionResultIntegrationEvent), Group = ConsumerName)]
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
            sender,
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
    ISender sender)
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

    [CapSubscribe(nameof(WorkOrderReleasedIntegrationEvent), Group = ConsumerName)]
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
            sender,
            integrationEvent,
            SchedulingPlanInvalidationReasons.WorkOrderReleased,
            affectedWorkOrderId: integrationEvent.Payload.WorkOrderId,
            affectedSkuCode: integrationEvent.Payload.SkuCode,
            cancellationToken: cancellationToken);
    }
}

internal static class SchedulingPlanInvalidationService
{
    public static Task InvalidateGeneratedPlansByWorkCenterAsync<TIntegrationEvent>(
        ISender sender,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string affectedResourceId,
        ILogger logger,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        return InvalidateGeneratedPlansByScopeAsync(
            sender,
            integrationEvent,
            reasonCode,
            SchedulePlanInvalidationScope.GeneratedWorkCenter,
            affectedResourceId,
            logger,
            cancellationToken);
    }

    public static Task InvalidateGeneratedPlansByCalendarAsync<TIntegrationEvent>(
        ISender sender,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string calendarId,
        ILogger logger,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        return InvalidateGeneratedPlansByScopeAsync(
            sender,
            integrationEvent,
            reasonCode,
            SchedulePlanInvalidationScope.GeneratedCalendar,
            calendarId,
            logger,
            cancellationToken);
    }

    private static async Task InvalidateGeneratedPlansByScopeAsync<TIntegrationEvent>(
        ISender sender,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        SchedulePlanInvalidationScope scope,
        string scopeValue,
        ILogger logger,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        if (string.IsNullOrWhiteSpace(scopeValue))
        {
            logger.LogInformation(
                "Scheduling input change {EventType} has no traceable scope value in {OrganizationId}/{EnvironmentId}; no schedule plan was invalidated.",
                integrationEvent.EventType,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
            return;
        }

        var normalizedScopeValue = scopeValue.Trim();
        var result = await sender.Send(
            ToCommand(
                integrationEvent,
                reasonCode,
                scope,
                normalizedScopeValue,
                affectedWorkOrderId: null,
                affectedSkuCode: null),
            cancellationToken);
        if (result.MatchedPlanCount == 0)
        {
            logger.LogInformation(
                "Scheduling input change {EventType} for scope {ScopeValue} matched no schedule plan in {OrganizationId}/{EnvironmentId}.",
                integrationEvent.EventType,
                normalizedScopeValue,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
        }
    }

    public static async Task InvalidateByResourceAsync<TIntegrationEvent>(
        ISender sender,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string affectedResourceId,
        ILogger logger,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        var normalizedResourceId = Required(affectedResourceId, nameof(affectedResourceId));
        var result = await sender.Send(
            ToCommand(
                integrationEvent,
                reasonCode,
                SchedulePlanInvalidationScope.Resource,
                normalizedResourceId,
                affectedWorkOrderId: null,
                affectedSkuCode: null),
            cancellationToken);
        if (result.MatchedPlanCount == 0)
        {
            logger.LogInformation(
                "Scheduling input change {EventType} for resource {AffectedResourceId} matched no schedule plan in {OrganizationId}/{EnvironmentId}.",
                integrationEvent.EventType,
                normalizedResourceId,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId);
        }
    }

    public static async Task InvalidateByWorkOrderOrOperationAsync<TIntegrationEvent>(
        ISender sender,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string sourceDocumentId,
        string? affectedSkuCode,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        var normalizedSource = Required(sourceDocumentId, nameof(sourceDocumentId));
        await sender.Send(
            ToCommand(
                integrationEvent,
                reasonCode,
                SchedulePlanInvalidationScope.WorkOrderOrOperation,
                normalizedSource,
                affectedWorkOrderId: null,
                affectedSkuCode),
            cancellationToken);
    }

    public static async Task InvalidateAllGeneratedPlansAsync<TIntegrationEvent>(
        ISender sender,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string? affectedWorkOrderId = null,
        string? affectedSkuCode = null,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        await sender.Send(
            ToCommand(
                integrationEvent,
                reasonCode,
                SchedulePlanInvalidationScope.AllInvalidatablePlans,
                scopeValue: null,
                affectedWorkOrderId,
                affectedSkuCode),
            cancellationToken);
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }

    private static RecordSchedulePlanInvalidationsCommand ToCommand<TIntegrationEvent>(
        TIntegrationEvent integrationEvent,
        string reasonCode,
        SchedulePlanInvalidationScope scope,
        string? scopeValue,
        string? affectedWorkOrderId,
        string? affectedSkuCode)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        return new RecordSchedulePlanInvalidationsCommand(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.EventId,
            integrationEvent.EventType,
            integrationEvent.SourceService,
            integrationEvent.OccurredAtUtc,
            reasonCode,
            scope,
            scopeValue,
            affectedWorkOrderId,
            affectedSkuCode);
    }
}

internal static class SchedulingProcessedIntegrationEventInbox
{
    public static Task<int> SaveChangesOrIgnoreDuplicateAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<ProcessedIntegrationEvent>(
            dbContext,
            dbContext.SaveChangesAsync,
            cancellationToken);
    }

    public static Task<bool> TryRecordByEventIdAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return TryRecordAsync(
            dbContext,
            consumerName,
            new EventInstanceInboxEnvelope(integrationEvent),
            cancellationToken);
    }

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

    private sealed class EventInstanceInboxEnvelope(IIntegrationEventEnvelope source) : IIntegrationEventEnvelope
    {
        public string EventId => source.EventId;
        public string EventType => source.EventType;
        public int EventVersion => source.EventVersion;
        public DateTimeOffset OccurredAtUtc => source.OccurredAtUtc;
        public string SourceService => source.SourceService;
        public string CorrelationId => source.CorrelationId;
        public string CausationId => source.CausationId;
        public string OrganizationId => source.OrganizationId;
        public string EnvironmentId => source.EnvironmentId;
        public string Actor => source.Actor;
        public string IdempotencyKey => source.EventId;
        public object? PayloadObject => source.PayloadObject;
    }
}
