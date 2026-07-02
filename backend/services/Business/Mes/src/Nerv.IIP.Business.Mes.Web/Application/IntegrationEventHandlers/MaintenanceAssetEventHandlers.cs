using DotNetCore.CAP;
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
    IMesPlanningStore store,
    RuleScheduler scheduler,
    MesRescheduleOptions options,
    ApplicationDbContext dbContext,
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

    [CapSubscribe("Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        var workCenterId = await store.ResolveWorkCenterIdAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.DeviceAssetId,
            cancellationToken);
        store.AddUnavailability(new WorkCenterUnavailability(
            workCenterId,
            payload.FromUtc,
            null,
            payload.Reason,
            payload.DeviceAssetId,
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId));

        if (options.AutoRescheduleOnAssetUnavailable)
        {
            var plan = scheduler.Schedule(
                await store.GetScheduleOperationsAsync(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken),
                await store.GetUnavailabilitiesAsync(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken));
            await store.AddScheduleResultAsync(RescheduleTrigger.AssetUnavailable, integrationEvent.OccurredAtUtc, plan, cancellationToken: cancellationToken);
        }
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

    [CapSubscribe("Nerv.IIP.Contracts.Maintenance.AssetRestoredIntegrationEvent", Group = ConsumerName)]
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
