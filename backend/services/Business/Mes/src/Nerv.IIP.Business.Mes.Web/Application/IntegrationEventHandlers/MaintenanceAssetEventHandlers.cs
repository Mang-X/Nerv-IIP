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

    public Task HandleAsync(AssetUnavailableIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;
        var workCenterId = store.ResolveWorkCenterId(payload.DeviceAssetId);
        store.AddUnavailability(new WorkCenterUnavailability(
            workCenterId,
            payload.FromUtc,
            null,
            payload.Reason,
            payload.DeviceAssetId));

        if (options.AutoRescheduleOnAssetUnavailable)
        {
            var plan = scheduler.Schedule(
                store.GetScheduleOperations(integrationEvent.OrganizationId, integrationEvent.EnvironmentId),
                store.Unavailabilities);
            store.AddScheduleResult(RescheduleTrigger.AssetUnavailable, integrationEvent.OccurredAtUtc, plan);
        }

        return Task.CompletedTask;
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

    public Task HandleAsync(AssetRestoredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;
        store.CloseUnavailability(payload.DeviceAssetId, payload.RestoredAtUtc);

        if (options.AutoRescheduleOnAssetRestored)
        {
            var plan = scheduler.Schedule(
                store.GetScheduleOperations(integrationEvent.OrganizationId, integrationEvent.EnvironmentId),
                store.Unavailabilities);
            store.AddScheduleResult(RescheduleTrigger.AssetRestored, integrationEvent.OccurredAtUtc, plan);
        }

        return Task.CompletedTask;
    }
}
