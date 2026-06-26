using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
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
    public void Stock_status_is_normalized_to_canonical_values()
    {
        var ledger = StockLedger.Create(
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

        Assert.Equal("unrestricted", ledger.QualityStatus);
    }

    [Fact]
    public void Unsupported_stock_status_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "random-text",
            "company",
            "owner-001"));
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
    public void Reservation_reduces_available_quantity_and_release_restores_it()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var reservation = StockReservation.Reserve(
            ledger,
            "mes",
            "WO-001",
            "LINE-001",
            "idem-reserve-001",
            4m);

        ledger.Reserve(reservation);

        Assert.Equal(4m, ledger.ReservedQuantity);
        Assert.Equal(6m, ledger.AvailableQuantity);

        ledger.ReleaseReservation(reservation, 1.5m);

        Assert.Equal(2.5m, ledger.ReservedQuantity);
        Assert.Equal(7.5m, ledger.AvailableQuantity);
        Assert.Equal("partially-released", reservation.Status);
    }

    [Fact]
    public void Reservation_cannot_exceed_available_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 2m, "idem-in-001"));
        var reservation = StockReservation.Reserve(
            ledger,
            "mes",
            "WO-001",
            "LINE-001",
            "idem-reserve-001",
            3m);

        var exception = Assert.Throws<InventoryDomainException>(() => ledger.Reserve(reservation));

        Assert.Equal(InventoryDomainFailureReason.ReservationAllocationRejected, exception.Reason);
    }

    [Fact]
    public void Outbound_allocation_consumes_reserved_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var reservation = StockReservation.Reserve(
            ledger,
            "mes",
            "WO-001",
            "LINE-001",
            "idem-reserve-001",
            4m);
        ledger.Reserve(reservation);

        ledger.AllocateReservation(reservation, 3m);
        ledger.ApplyMovement(NewMovement("outbound", -3m, "idem-out-001"));

        Assert.Equal(7m, ledger.OnHandQuantity);
        Assert.Equal(1m, ledger.ReservedQuantity);
        Assert.Equal(6m, ledger.AvailableQuantity);
        Assert.Equal("partially-allocated", reservation.Status);
    }

    [Theory]
    [InlineData("allocate")]
    [InlineData("release")]
    public void Reservation_operation_with_inconsistent_ledger_reserved_quantity_reports_structured_failure(string operation)
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var reservation = StockReservation.Reserve(
            ledger,
            "mes",
            "WO-001",
            "LINE-001",
            "idem-reserve-001",
            4m);

        var exception = Assert.Throws<InventoryDomainException>(() =>
        {
            if (operation == "allocate")
            {
                ledger.AllocateReservation(reservation, 1m);
                return;
            }

            ledger.ReleaseReservation(reservation, 1m);
        });

        Assert.Equal(InventoryDomainFailureReason.ReservationAllocationRejected, exception.Reason);
    }

    [Fact]
    public void Unreserved_outbound_cannot_reduce_on_hand_below_reserved_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var reservation = StockReservation.Reserve(
            ledger,
            "mes",
            "WO-001",
            "LINE-001",
            "idem-reserve-001",
            8m);
        ledger.Reserve(reservation);

        var exception = Assert.Throws<InventoryDomainException>(() =>
            ledger.ApplyMovement(NewMovement("outbound", -3m, "idem-out-001")));

        Assert.Equal(InventoryDomainFailureReason.CommittedStockProtection, exception.Reason);
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(8m, ledger.ReservedQuantity);
        Assert.Equal(2m, ledger.AvailableQuantity);
    }

    [Fact]
    public void Moving_average_valuation_updates_ledger_value()
    {
        var ledger = NewLedger();

        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001", unitCost: 2m));
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-002", unitCost: 4m));
        var outbound = NewMovement("outbound", -5m, "idem-out-001");
        ledger.ApplyMovement(outbound);

        Assert.Equal(3m, ledger.MovingAverageUnitCost);
        Assert.Equal(45m, ledger.InventoryValue);
        Assert.Equal(3m, outbound.UnitCost);
        Assert.Equal(-15m, outbound.MovementAmount);
    }

    [Fact]
    public void Moving_average_valuation_rounds_to_storage_precision()
    {
        var ledger = NewLedger();

        ledger.ApplyMovement(NewMovement("inbound", 1m, "idem-in-001", unitCost: 1m));
        ledger.ApplyMovement(NewMovement("inbound", 2m, "idem-in-002", unitCost: 2m));

        Assert.Equal(1.666667m, ledger.MovingAverageUnitCost);
        Assert.Equal(5m, ledger.InventoryValue);
    }

    [Fact]
    public void Outbound_movement_ignores_external_unit_cost_and_uses_moving_average()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001", unitCost: 2m));
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-002", unitCost: 4m));
        var outbound = NewMovement("outbound", -5m, "idem-out-001", unitCost: 99m);

        ledger.ApplyMovement(outbound);

        Assert.Equal(3m, outbound.UnitCost);
        Assert.Equal(-15m, outbound.MovementAmount);
        Assert.Equal(45m, ledger.InventoryValue);
        Assert.Equal(3m, ledger.MovingAverageUnitCost);
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

        var exception = Assert.Throws<InventoryDomainException>(() => ledger.ApplyMovement(conflicting));

        Assert.Equal(InventoryDomainFailureReason.IdempotencyConflict, exception.Reason);
    }

    [Fact]
    public void Outbound_movement_that_would_make_on_hand_negative_is_rejected()
    {
        var ledger = NewLedger();

        var outbound = NewMovement("outbound", -1m, "idem-out-001");

        var exception = Assert.Throws<InventoryDomainException>(() => ledger.ApplyMovement(outbound));

        Assert.Equal(InventoryDomainFailureReason.NegativeOnHand, exception.Reason);
    }

    [Fact]
    public void Zero_quantity_movement_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewMovement("inbound", 0m, "idem-zero-001"));
    }

    [Fact]
    public void Movement_with_mismatched_dimensions_is_rejected()
    {
        var ledger = NewLedger();
        var movement = StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-001",
            "LINE-001",
            "idem-other-location-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            1m);

        var exception = Assert.Throws<InventoryDomainException>(() => ledger.ApplyMovement(movement));

        Assert.Equal(InventoryDomainFailureReason.DimensionMismatch, exception.Reason);
    }

    [Fact]
    public void Count_task_rejects_duplicate_confirmation()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var task = NewCountTask(ledger);

        task.ConfirmAdjustment(ledger, countedQuantity: 7.5m, "idem-count-001");

        Assert.Throws<InvalidOperationException>(() => task.ConfirmAdjustment(ledger, countedQuantity: 7m, "idem-count-002"));
    }

    [Fact]
    public void Decimal_quantity_supports_six_fractional_digits()
    {
        var ledger = NewLedger();
        var movement = NewMovement("inbound", 0.000001m, "idem-decimal-001");

        ledger.ApplyMovement(movement);

        Assert.Equal(0.000001m, ledger.OnHandQuantity);
    }

    [Fact]
    public void Count_adjustment_creates_adjustment_movement_and_updates_ledger_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var task = NewCountTask(ledger);

        var adjustment = task.ConfirmAdjustment(ledger, countedQuantity: 7.5m, "idem-count-001");

        Assert.Equal("count-adjustment", adjustment.MovementType);
        Assert.Equal(-2.5m, adjustment.Quantity);
        Assert.Equal(7.5m, ledger.OnHandQuantity);
        Assert.IsType<StockCountVarianceConfirmedDomainEvent>(task.GetDomainEvents().Single());
    }

    [Fact]
    public void Negative_count_adjustment_cannot_reduce_on_hand_below_reserved_quantity()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var reservation = StockReservation.Reserve(
            ledger,
            "mes",
            "WO-001",
            "LINE-001",
            "idem-reserve-001",
            8m);
        ledger.Reserve(reservation);
        var task = NewCountTask(ledger);

        var exception = Assert.Throws<InventoryDomainException>(() =>
            task.ConfirmAdjustment(ledger, countedQuantity: 7m, "idem-count-001"));

        Assert.Equal(InventoryDomainFailureReason.CommittedStockProtection, exception.Reason);
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(8m, ledger.ReservedQuantity);
        Assert.Equal(2m, ledger.AvailableQuantity);
        Assert.Equal("open", task.Status);
    }

    [Fact]
    public void Open_count_task_freezes_ledger_against_regular_movements()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        ledger.FreezeForCount("COUNT-001");

        var exception = Assert.Throws<InventoryDomainException>(() =>
            ledger.ApplyMovement(NewMovement("outbound", -1m, "idem-out-001")));

        Assert.Equal(InventoryDomainFailureReason.LedgerFrozen, exception.Reason);
        Assert.Contains("frozen", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Count_adjustment_requires_expected_ledger_version()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var task = NewCountTask(ledger);
        ledger.ApplyMovement(NewMovement("inbound", 2m, "idem-in-002"));

        var exception = Assert.Throws<StockCountRecountRequiredException>(() =>
            task.ConfirmAdjustment(ledger, countedQuantity: 9m, "idem-count-001"));

        Assert.Contains("recount", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("recount-required", task.Status);
    }

    [Fact]
    public void Count_task_cancel_releases_ledger_freeze()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var task = NewCountTask(ledger);
        ledger.FreezeForCount(task.CountTaskCode);

        task.Cancel(ledger, "operator-cancelled");

        Assert.Equal("cancelled", task.Status);
        Assert.False(ledger.IsFrozenForCount);
        ledger.ApplyMovement(NewMovement("outbound", -1m, "idem-out-001"));
        Assert.Equal(9m, ledger.OnHandQuantity);
    }

    [Fact]
    public void Count_adjustment_fact_requires_assigned_movement_id()
    {
        var ledger = NewLedger();
        ledger.ApplyMovement(NewMovement("inbound", 10m, "idem-in-001"));
        var task = NewCountTask(ledger);
        var movement = task.ConfirmAdjustment(ledger, countedQuantity: 7.5m, "idem-count-001");

        Assert.Throws<ArgumentException>(() => StockCountAdjustment.Record(task, movement, "idem-count-001"));
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

    private static StockMovement NewMovement(string movementType, decimal quantity, string idempotencyKey, decimal? unitCost = null)
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
            quantity,
            unitCost);
    }

    private static StockCountTask NewCountTask(StockLedger ledger)
    {
        return StockCountTask.Create(
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
    }
}
