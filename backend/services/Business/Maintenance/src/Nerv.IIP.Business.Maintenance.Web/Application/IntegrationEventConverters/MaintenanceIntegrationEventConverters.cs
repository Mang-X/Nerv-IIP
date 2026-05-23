using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.Maintenance;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;

public sealed class MaintenanceWorkOrderOpenedIntegrationEventConverter
    : IIntegrationEventConverter<MaintenanceWorkOrderOpenedDomainEvent, MaintenanceWorkOrderOpenedIntegrationEvent>
{
    public MaintenanceWorkOrderOpenedIntegrationEvent Convert(MaintenanceWorkOrderOpenedDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        return new MaintenanceWorkOrderOpenedIntegrationEvent(
            EventIds.New(),
            MaintenanceLocalIntegrationEventTypes.WorkOrderOpened,
            MaintenanceIntegrationEventVersions.V1,
            workOrder.OpenedAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            workOrder.Id.ToString(),
            workOrder.SourceAlarmId ?? workOrder.Id.ToString(),
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            $"maintenance-work-order-opened:{workOrder.Id}",
            new MaintenanceWorkOrderOpenedPayload(workOrder.Id.ToString(), workOrder.DeviceAssetId, workOrder.SourceAlarmId, workOrder.Priority));
    }
}

public sealed class MaintenanceWorkOrderCompletedIntegrationEventConverter
    : IIntegrationEventConverter<MaintenanceWorkOrderCompletedDomainEvent, MaintenanceWorkOrderCompletedIntegrationEvent>
{
    public MaintenanceWorkOrderCompletedIntegrationEvent Convert(MaintenanceWorkOrderCompletedDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        return new MaintenanceWorkOrderCompletedIntegrationEvent(
            EventIds.New(),
            MaintenanceLocalIntegrationEventTypes.WorkOrderCompleted,
            MaintenanceIntegrationEventVersions.V1,
            workOrder.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            MaintenanceIntegrationEventSources.Maintenance,
            workOrder.Id.ToString(),
            workOrder.SourceAlarmId ?? workOrder.Id.ToString(),
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            $"maintenance-work-order-completed:{workOrder.Id}",
            new MaintenanceWorkOrderCompletedPayload(workOrder.Id.ToString(), workOrder.DeviceAssetId, workOrder.DowntimeMinutes ?? 0));
    }
}

public sealed class AssetUnavailableIntegrationEventConverter
    : IIntegrationEventConverter<AssetUnavailableDomainEvent, AssetUnavailableIntegrationEvent>
{
    public AssetUnavailableIntegrationEvent Convert(AssetUnavailableDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        return new AssetUnavailableIntegrationEvent(
            EventIds.New(),
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            domainEvent.FromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            workOrder.Id.ToString(),
            workOrder.SourceAlarmId ?? workOrder.Id.ToString(),
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            $"asset-unavailable:{workOrder.Id}:{domainEvent.FromUtc:O}",
            new AssetUnavailablePayload(workOrder.DeviceAssetId, domainEvent.Reason, domainEvent.FromUtc));
    }
}

public sealed class AssetRestoredIntegrationEventConverter
    : IIntegrationEventConverter<AssetRestoredDomainEvent, AssetRestoredIntegrationEvent>
{
    public AssetRestoredIntegrationEvent Convert(AssetRestoredDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        return new AssetRestoredIntegrationEvent(
            EventIds.New(),
            MaintenanceIntegrationEventTypes.AssetRestored,
            MaintenanceIntegrationEventVersions.V1,
            domainEvent.RestoredAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            workOrder.Id.ToString(),
            workOrder.SourceAlarmId ?? workOrder.Id.ToString(),
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            $"asset-restored:{workOrder.Id}:{domainEvent.RestoredAtUtc:O}",
            new AssetRestoredPayload(workOrder.DeviceAssetId, domainEvent.RestoredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";
}
