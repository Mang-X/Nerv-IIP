using Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;

public sealed class StockMovementPostedIntegrationEventConverter(IInventoryIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<StockMovementPostedDomainEvent, StockMovementPostedIntegrationEvent>
{
    public StockMovementPostedIntegrationEvent Convert(StockMovementPostedDomainEvent domainEvent)
    {
        var movement = domainEvent.StockMovement;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        var movementId = movement.Id
            ?? throw new InvalidOperationException("Stock movement id must be assigned before publishing StockMovementPostedIntegrationEvent.");
        return new StockMovementPostedIntegrationEvent(
            EventIds.New(),
            InventoryIntegrationEventTypes.StockMovementPosted,
            1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessInventory,
            context.CorrelationId,
            context.CausationId,
            movement.OrganizationId,
            movement.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("stock-movement-posted", movement.OrganizationId, movement.EnvironmentId, movement.SourceService, movement.SourceDocumentId, movement.IdempotencyKey),
            new StockMovementPostedPayload(
                movementId.ToString(),
                movement.MovementType,
                movement.SourceService,
                movement.SourceDocumentId,
                movement.SourceDocumentLineId,
                movement.IdempotencyKey,
                movement.SkuCode,
                movement.UomCode,
                movement.SiteCode,
                movement.LocationCode,
                movement.LotNo,
                movement.SerialNo,
                movement.QualityStatus,
                movement.OwnerType,
                movement.OwnerId,
                movement.Quantity,
                movement.PostedAtUtc,
                movement.UnitCost,
                movement.MovementAmount));
    }
}

public sealed class StockCountVarianceConfirmedIntegrationEventConverter(IInventoryIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<StockCountVarianceConfirmedDomainEvent, StockCountVarianceConfirmedIntegrationEvent>
{
    public StockCountVarianceConfirmedIntegrationEvent Convert(StockCountVarianceConfirmedDomainEvent domainEvent)
    {
        var task = domainEvent.StockCountTask;
        var movement = domainEvent.AdjustmentMovement;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new StockCountVarianceConfirmedIntegrationEvent(
            EventIds.New(),
            InventoryIntegrationEventTypes.StockCountVarianceConfirmed,
            1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessInventory,
            context.CorrelationId,
            context.CausationId,
            task.OrganizationId,
            task.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("stock-count-variance-confirmed", task.OrganizationId, task.EnvironmentId, task.CountTaskCode),
            new StockCountVarianceConfirmedPayload(
                task.CountTaskCode,
                task.SkuCode,
                task.UomCode,
                task.SiteCode,
                task.LocationCode,
                task.CountedQuantity,
                task.VarianceQuantity ?? 0,
                occurredAtUtc));
    }
}

public sealed class StockAvailabilityChangedIntegrationEventConverter(IInventoryIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<StockAvailabilityChangedDomainEvent, StockAvailabilityChangedIntegrationEvent>
{
    public StockAvailabilityChangedIntegrationEvent Convert(StockAvailabilityChangedDomainEvent domainEvent)
    {
        var ledger = domainEvent.StockLedger;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new StockAvailabilityChangedIntegrationEvent(
            EventIds.New(),
            InventoryIntegrationEventTypes.StockAvailabilityChanged,
            1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessInventory,
            context.CorrelationId,
            context.CausationId,
            ledger.OrganizationId,
            ledger.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("stock-availability-changed", ledger.OrganizationId, ledger.EnvironmentId, ledger.SkuCode, ledger.SiteCode, ledger.LocationCode, ledger.LedgerVersion.ToString()),
            new StockAvailabilityChangedPayload(
                ledger.SkuCode,
                ledger.UomCode,
                ledger.SiteCode,
                ledger.LocationCode,
                ledger.LotNo,
                ledger.SerialNo,
                ledger.QualityStatus,
                ledger.OwnerType,
                ledger.OwnerId,
                ledger.OnHandQuantity,
                ledger.ReservedQuantity,
                ledger.AvailableQuantity,
                ledger.LedgerVersion,
                occurredAtUtc,
                ledger.MovingAverageUnitCost,
                ledger.InventoryValue));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"inventory:{string.Join(':', parts)}";

}
