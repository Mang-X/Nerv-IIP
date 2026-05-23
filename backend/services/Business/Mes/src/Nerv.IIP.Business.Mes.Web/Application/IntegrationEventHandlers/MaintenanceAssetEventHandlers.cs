using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Maintenance;
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
    MesRescheduleOptions options)
    : IIntegrationEventHandler<AssetUnavailableIntegrationEvent>
{
    public const string ConsumerName = "business-mes.asset-unavailable";

    public async Task HandleAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
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
    MesRescheduleOptions options)
    : IIntegrationEventHandler<AssetRestoredIntegrationEvent>
{
    public const string ConsumerName = "business-mes.asset-restored";

    public async Task HandleAsync(AssetRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
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
