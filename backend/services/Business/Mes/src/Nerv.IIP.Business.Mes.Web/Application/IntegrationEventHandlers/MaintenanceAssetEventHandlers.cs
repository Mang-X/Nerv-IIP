using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

public sealed class MesRescheduleOptions
{
    public bool AutoRescheduleOnAssetUnavailable { get; set; } = true;

    public bool AutoRescheduleOnAssetRestored { get; set; } = true;
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent", ConsumerName)]
public sealed class AssetUnavailableIntegrationEventHandlerForReschedule(
    MesAssetUnavailableCanonicalProcessor processor,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<AssetUnavailableIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.asset-unavailable";

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
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;
        await processor.ProcessAsync(
            integrationEvent,
            payload.DeviceAssetId,
            payload.Reason,
            payload.FromUtc,
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetUnavailableV2IntegrationEvent", ConsumerName)]
public sealed class AssetUnavailableV2IntegrationEventHandlerForReschedule(
    MesAssetUnavailableCanonicalProcessor processor,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<AssetUnavailableV2IntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName;

    private readonly IntegrationEventConsumerGuard<AssetUnavailableV2IntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2));

    public Task HandleAsync(AssetUnavailableV2IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(AssetUnavailableIntegrationEventTopics.V2Template, Group = ConsumerName)]
    public Task HandleCapAsync(AssetUnavailableV2IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task HandleValidEventAsync(
        AssetUnavailableV2IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        try
        {
            AssetUnavailableIntegrationEventTopics.EnsureV2EnvelopeMatches(
                AssetUnavailableIntegrationEventTopics.V2("validation"),
                integrationEvent);
        }
        catch (JsonException exception)
        {
            return deadLetterStore.AddAsync(
                CreateInvalidEnvelopeDeadLetter(integrationEvent, exception.Message),
                cancellationToken);
        }

        var payload = integrationEvent.Payload;
        return processor.ProcessAsync(
            integrationEvent,
            payload.DeviceAssetId,
            payload.ReasonCode,
            payload.FromUtc,
            cancellationToken);
    }

    private static IntegrationEventDeadLetterMessage CreateInvalidEnvelopeDeadLetter(
        AssetUnavailableV2IntegrationEvent integrationEvent,
        string failureMessage) =>
        new(
            Guid.CreateVersion7(),
            ConsumerName,
            integrationEvent.EventId,
            integrationEvent.EventType,
            integrationEvent.EventVersion,
            integrationEvent.SourceService,
            integrationEvent.IdempotencyKey,
            typeof(AssetUnavailableV2IntegrationEvent).FullName ?? nameof(AssetUnavailableV2IntegrationEvent),
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
                integrationEvent.Payload,
            }),
            "invalid-envelope",
            failureMessage,
            IntegrationEventDeadLetterStatus.Pending,
            DateTimeOffset.UtcNow,
            null);
}

public sealed class MesAssetUnavailableCanonicalProcessor(
    IMesPlanningStore store,
    RuleScheduler scheduler,
    MesRescheduleOptions options,
    ApplicationDbContext dbContext)
{
    public async Task ProcessAsync(
        IIntegrationEventEnvelope integrationEvent,
        string deviceAssetId,
        string reason,
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
                integrationEvent,
                cancellationToken))
        {
            return;
        }

        var workCenterId = await store.ResolveWorkCenterIdAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            deviceAssetId,
            cancellationToken);
        store.AddUnavailability(new WorkCenterUnavailability(
            workCenterId,
            fromUtc,
            null,
            reason,
            deviceAssetId,
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId));

        if (options.AutoRescheduleOnAssetUnavailable)
        {
            var plan = scheduler.Schedule(
                await store.GetScheduleOperationsAsync(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken),
                await store.GetUnavailabilitiesAsync(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken));
            await store.AddScheduleResultAsync(
                RescheduleTrigger.AssetUnavailable,
                integrationEvent.OccurredAtUtc,
                plan,
                cancellationToken: cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Maintenance.AssetRestoredIntegrationEvent", ConsumerName)]
public sealed class AssetRestoredIntegrationEventHandlerForReschedule(
    IMesPlanningStore store,
    RuleScheduler scheduler,
    MesRescheduleOptions options,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<AssetRestoredIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.asset-restored";

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
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        await store.CloseUnavailabilityAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.DeviceAssetId,
            payload.RestoredAtUtc,
            cancellationToken);

        if (options.AutoRescheduleOnAssetRestored)
        {
            var plan = scheduler.Schedule(
                await store.GetScheduleOperationsAsync(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken),
                await store.GetUnavailabilitiesAsync(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken));
            await store.AddScheduleResultAsync(RescheduleTrigger.AssetRestored, integrationEvent.OccurredAtUtc, plan, cancellationToken: cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal static class MesProcessedIntegrationEventInbox
{
    public static Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return TryRecordBothIdentitiesAsync(dbContext, consumerName, integrationEvent, cancellationToken);
    }

    private static async Task<bool> TryRecordBothIdentitiesAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationEvent.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationEvent.IdempotencyKey);

        var isAlreadyClaimed = dbContext.ProcessedIntegrationEvents.Local.Any(processed =>
            processed.ConsumerName == consumerName &&
            (processed.EventId == integrationEvent.EventId ||
             processed.IdempotencyKey == integrationEvent.IdempotencyKey));
        if (isAlreadyClaimed || await dbContext.ProcessedIntegrationEvents.AnyAsync(
                processed =>
                    processed.ConsumerName == consumerName &&
                    (processed.EventId == integrationEvent.EventId ||
                     processed.IdempotencyKey == integrationEvent.IdempotencyKey),
                cancellationToken))
        {
            return false;
        }

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
}
