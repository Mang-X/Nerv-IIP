using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;

namespace Nerv.IIP.Business.Inventory.Domain.Tests;

public sealed class InventoryAggregateTests
{
    [Fact]
    public void Inbound_movement_increases_on_hand_quantity_and_raises_events()
    {
        var ledger = NewLedger();
        var movement = NewMovement("inbound", 12.5m, "idem-in-001");

        ledger.ApplyMovement(movement);

        Assert.Equal(12.5m, ledger.OnHandQuantity);
        Assert.Equal(0, ledger.ReservedQuantity);
        Assert.Equal(12.5m, ledger.AvailableQuantity);
        Assert.IsType<StockMovementPostedDomainEvent>(movement.GetDomainEvents().Single());
        Assert.IsType<StockAvailabilityChangedDomainEvent>(ledger.GetDomainEvents().Single());
    }

    [Fact]
    public void Outbound_movement_decreases_on_hand_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 20m, "idem-in-001"));
        ledger.ClearDomainEvents();

        ledger.ApplyMovement(NewMovement("outbound", -3.25m, "idem-out-001"));

        Assert.Equal(16.75m, ledger.OnHandQuantity);
        Assert.Equal(16.75m, ledger.AvailableQuantity);
    }

    [Fact]
    public void Duplicate_idempotency_key_with_same_payload_returns_existing_movement()
    {
        var ledger = NewLedger();
        var first = NewMovement("inbound", 5m, "idem-001");
        ledger.ApplyMovement(first);

        var duplicate = NewMovement("inbound", 5m, "idem-001");
        var result = ledger.ApplyMovement(duplicate);

        Assert.Same(first, result);
        Assert.Equal(5m, ledger.OnHandQuantity);
    }

    [Fact]
    public void Duplicate_idempotency_key_with_different_payload_is_rejected()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 5m, "idem-001"));

        var conflicting = NewMovement("inbound", 6m, "idem-001");

        Assert.Throws<InvalidOperationException>(() => ledger.ApplyMovement(conflicting));
    }

    [Fact]
    public void Outbound_movement_that_would_make_on_hand_negative_is_rejected()
    {
        var ledger = NewLedger();

        var outbound = NewMovement("outbound", -1m, "idem-out-001");

        Assert.Throws<InvalidOperationException>(() => ledger.ApplyMovement(outbound));
    }

    [Fact]
    public void Count_adjustment_creates_adjustment_movement_and_updates_ledger_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var task = StockCountTask.Create(
            "org-001",
            "env-dev",
            "COUNT-001",
            ledger.OrganizationId,
            ledger.EnvironmentId,
            ledger.SkuCode,
            ledger.UomCode,
            ledger.SiteCode,
            ledger.LocationCode,
            ledger.LotNo,
            ledger.SerialNo,
            ledger.QualityStatus,
            ledger.OwnerType,
            ledger.OwnerId,
            ledger.LedgerVersion);

        var adjustment = task.ConfirmAdjustment(ledger, countedQuantity: 7.5m, "idem-count-001");
        var adjustmentFact = StockCountAdjustment.Record(task, adjustment, "idem-count-001");

        Assert.Equal("count-adjustment", adjustment.MovementType);
        Assert.Equal(-2.5m, adjustment.Quantity);
        Assert.Equal(-2.5m, adjustmentFact.VarianceQuantity);
        Assert.Equal(7.5m, adjustmentFact.CountedQuantity);
        Assert.Equal(7.5m, ledger.OnHandQuantity);
        Assert.IsType<StockCountVarianceConfirmedDomainEvent>(task.GetDomainEvents().Single());
    }

    private static StockLedger NewLedger()
    {
        return StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
    }

    private static StockMovement NewMovement(string movementType, decimal quantity, string idempotencyKey)
    {
        return StockMovement.Post(
            "org-001",
            "env-dev",
            movementType,
            "wms",
            "DOC-001",
            "LINE-001",
            idempotencyKey,
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            quantity);
    }
}
