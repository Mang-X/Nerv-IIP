using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
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

public sealed class MaintenanceSparePartIssuedIntegrationEventConverter
    : IIntegrationEventConverter<MaintenanceSparePartIssuedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(MaintenanceSparePartIssuedDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        var line = domainEvent.SparePartLine;
        var occurredAtUtc = workOrder.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        // Never invent a unit of measure for a spare-part issue. A guessed unit is either unknown to the
        // unit master data (the movement fails downstream) or belongs to another dimension (the movement
        // silently posts against the wrong ledger quantity). Missing units are a data defect at the source,
        // so surface them here instead of shipping a fabricated one on the integration event.
        var uomCode = line.UomCode?.Trim();
        if (string.IsNullOrEmpty(uomCode))
        {
            throw new InvalidOperationException(
                $"Maintenance spare part line '{line.Id}' on work order '{workOrder.Id}' has no unit of measure; " +
                "the inventory movement cannot be requested without the spare part's unit.");
        }

        // The key is derived from the work order + line only, so retries of the same issue stay idempotent
        // on the consumer side regardless of when the unit was filled in.
        var idempotencyKey = $"maintenance:{workOrder.OrganizationId}:{workOrder.EnvironmentId}:{workOrder.Id}:{line.Id}";
        return new InventoryMovementRequestedIntegrationEvent(
            EventIds.New(),
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            occurredAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            workOrder.Id.ToString(),
            line.Id.ToString(),
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                "outbound",
                "maintenance",
                workOrder.Id.ToString(),
                line.Id.ToString(),
                idempotencyKey,
                line.SkuCode,
                uomCode,
                "maintenance",
                "maintenance-spares",
                null,
                null,
                "available",
                "maintenance",
                null,
                -Math.Abs(line.Quantity),
                occurredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";
}
