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
            domainEvent.ChangedAtUtc ?? domainEvent.FromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            workOrder.Id.ToString(),
            workOrder.SourceAlarmId ?? workOrder.Id.ToString(),
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.OpenedBy,
            domainEvent.Revision is null
                ? $"asset-unavailable:{workOrder.Id}:{domainEvent.FromUtc:O}"
                : $"asset-unavailable:{workOrder.Id}:{domainEvent.FromUtc:O}:prediction:{domainEvent.Revision}",
            new AssetUnavailablePayload(workOrder.DeviceAssetId, domainEvent.Reason, domainEvent.FromUtc, domainEvent.ExpectedRestoreAtUtc));
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

        // 领出工厂与库位来自领用时选定的已登记库位（#3902）。以前写死 maintenance / maintenance-spares，
        // Inventory 拒收未登记库位后每一笔都会失败；缺失同样是源头数据缺陷，照单位的做法在这里显形。
        var siteCode = line.SiteCode?.Trim();
        var locationCode = line.LocationCode?.Trim();
        if (string.IsNullOrEmpty(siteCode) || string.IsNullOrEmpty(locationCode))
        {
            throw new InvalidOperationException(
                $"Maintenance spare part line '{line.Id}' on work order '{workOrder.Id}' has no issue site/location; " +
                "the inventory movement cannot be requested without a registered stock location.");
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
                InventoryMovementTypes.Outbound,
                // 载荷来源服务面（#1370 ③ 批次 D 销账）。
                InventoryMovementSourceServices.Maintenance,
                // 来源单据用维修工单的正式单号（#3852），库存流水与其它来源（GR-… / WO-…）一样显示人读单号；
                // 幂等键仍按工单 ID + 行 ID 派生，不受单号影响。
                workOrder.WorkOrderNo,
                line.Id.ToString(),
                idempotencyKey,
                line.SkuCode,
                uomCode,
                siteCode,
                locationCode,
                null,
                null,
                "available",
                // 备件是企业自有库存（采购收货按 company 入账）；维保是消耗方而非归属方。
                // 写成 maintenance 会去找一条不存在的维保归属台账，出库必然因负库存被拒（#3902）。
                "company",
                null,
                -Math.Abs(line.Quantity),
                occurredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";
}
