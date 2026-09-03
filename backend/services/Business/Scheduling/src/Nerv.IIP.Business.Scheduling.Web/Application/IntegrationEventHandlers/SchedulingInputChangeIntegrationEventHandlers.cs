using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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
public sealed class AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans
    : IIntegrationEventHandler<AssetUnavailableIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.asset-unavailable";

    private readonly IntegrationEventConsumerGuard<AssetUnavailableIntegrationEvent> consumerGuard;
    private readonly IAssetUnavailableCanonicalProcessor processor;

    public AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
        IIntegrationEventDeadLetterStore deadLetterStore,
        IAssetUnavailableCanonicalProcessor processor)
    {
        this.processor = processor;
        consumerGuard = new IntegrationEventConsumerGuard<AssetUnavailableIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(), deadLetterStore, new IntegrationEventConsumerOptions(
            ConsumerName,
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1));
    }

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
        await processor.ProcessAsync(new AssetUnavailableCanonicalInput(
            integrationEvent, integrationEvent.Payload.DeviceAssetId, integrationEvent.Payload.Reason), cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetUnavailableV2IntegrationEvent", AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName)]
public sealed class AssetUnavailableV2IntegrationEventHandlerForInvalidateSchedulePlans(
    IIntegrationEventDeadLetterStore deadLetterStore,
    IAssetUnavailableCanonicalProcessor processor)
    : IIntegrationEventHandler<AssetUnavailableV2IntegrationEvent>, ICapSubscribe
{
    private readonly IntegrationEventConsumerGuard<AssetUnavailableV2IntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore, new IntegrationEventConsumerOptions(
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2));

    public async Task HandleAsync(AssetUnavailableV2IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // 纵深防御，不是可达路径：v2 wire 契约的 converter 在 Read 时就拒绝非 business-maintenance 的 source，
        // 经 CAP 投递的 envelope 到不了这里；本分支只对进程内直接构造的对象生效，把它完整落进 DLQ 以便追查。
        if (!string.Equals(integrationEvent.SourceService, MaintenanceIntegrationEventSources.BusinessMaintenance, StringComparison.Ordinal))
        {
            await deadLetterStore.AddAsync(new IntegrationEventDeadLetterMessage(
                Guid.CreateVersion7(),
                AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
                integrationEvent.EventId,
                integrationEvent.EventType,
                integrationEvent.EventVersion,
                integrationEvent.SourceService,
                integrationEvent.IdempotencyKey,
                typeof(AssetUnavailableV2IntegrationEvent).FullName!,
                JsonSerializer.Serialize(new
                {
                    integrationEvent.EventId,
                    integrationEvent.EventType,
                    integrationEvent.EventVersion,
                    integrationEvent.OccurredAtUtc,
                    integrationEvent.SourceService,
                    integrationEvent.CorrelationId,
                    integrationEvent.CausationId,
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    integrationEvent.Actor,
                    integrationEvent.IdempotencyKey,
                    integrationEvent.Payload
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "unexpected-source-service",
                "AssetUnavailable v2 requires the business-maintenance source service.",
                IntegrationEventDeadLetterStatus.Pending,
                DateTimeOffset.UtcNow,
                null), cancellationToken);
            return;
        }
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(AssetUnavailableIntegrationEventTopics.V2Template, Group = AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName)]
    public Task HandleCapAsync(AssetUnavailableV2IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task HandleValidEventAsync(AssetUnavailableV2IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        processor.ProcessAsync(new AssetUnavailableCanonicalInput(
            integrationEvent, integrationEvent.Payload.DeviceAssetId, integrationEvent.Payload.ReasonCode), cancellationToken);
}

/// <summary>
/// v1/v2 归一后的处理输入。<paramref name="UpstreamReason"/> 只作为上游事实随输入传递（v1 的 Reason / v2 的 ReasonCode），
/// Scheduling 不复制 Maintenance 的原因目录、不解释其业务语义、也不据此分支；它存在是为了让 seam 上的观测
/// （日志、测试装饰器）能看到上游原样的原因，而不是被 Scheduling 改写过的版本。
/// </summary>
public sealed record AssetUnavailableCanonicalInput(
    IIntegrationEventEnvelope Envelope,
    string DeviceAssetId,
    string UpstreamReason);

public interface IAssetUnavailableCanonicalProcessor
{
    Task ProcessAsync(AssetUnavailableCanonicalInput input, CancellationToken cancellationToken);
}

public sealed class AssetUnavailableCanonicalProcessor(
    ISender sender,
    ILogger<AssetUnavailableCanonicalProcessor> logger) : IAssetUnavailableCanonicalProcessor
{
    public async Task ProcessAsync(
        AssetUnavailableCanonicalInput input,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ProcessAssetUnavailableCommand(input.Envelope, input.DeviceAssetId),
            cancellationToken);
        if (result.MatchedPlanCount == 0)
        {
            logger.LogInformation(
                "Scheduling input change {EventType} for resource {AffectedResourceId} matched no schedule plan in {OrganizationId}/{EnvironmentId}.",
                input.Envelope.EventType,
                input.DeviceAssetId,
                input.Envelope.OrganizationId,
                input.Envelope.EnvironmentId);
        }
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

    internal static RecordSchedulePlanInvalidationsCommand ToCommand<TIntegrationEvent>(
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

    /// <summary>
    /// 双身份 claim：同 consumer 下 EventId 或 IdempotencyKey 任一已被记录即视为重复。串行化由
    /// <see cref="IAssetUnavailableInboxIdentityLock"/>（Infrastructure）提供；两条唯一索引是最后一道防线，
    /// 冲突由 <c>ApplicationDbContext.SaveChanges</c> 吞成 0 行而不是抛出。
    /// </summary>
    public static async Task<bool> TryRecordAssetUnavailableAsync(
        ApplicationDbContext dbContext,
        IAssetUnavailableInboxIdentityLock identityLock,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        await identityLock.AcquireAsync(consumerName, integrationEvent, cancellationToken);

        if (dbContext.ProcessedIntegrationEvents.Local.Any(x =>
                x.ConsumerName == consumerName &&
                (x.EventId == integrationEvent.EventId || x.IdempotencyKey == integrationEvent.IdempotencyKey)) ||
            await dbContext.ProcessedIntegrationEvents.AnyAsync(x =>
                x.ConsumerName == consumerName &&
                (x.EventId == integrationEvent.EventId || x.IdempotencyKey == integrationEvent.IdempotencyKey),
                cancellationToken))
            return false;

        dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
            consumerName,
            integrationEvent.EventId,
            integrationEvent.EventType,
            integrationEvent.EventVersion,
            integrationEvent.SourceService,
            integrationEvent.IdempotencyKey,
            DateTimeOffset.UtcNow));
        return true;
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
