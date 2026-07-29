using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WmsExecutionAggregateTests
{
    [Fact]
    public void Open_inbound_expectation_can_be_cancelled_with_an_audit_reason()
    {
        var inbound = DomainWmsFactory.InboundOrder();

        inbound.Cancel("purchase-order-cancelled");

        Assert.Equal(InboundOrderStatus.Cancelled, inbound.Status);
        Assert.Equal("purchase-order-cancelled", inbound.CancellationReason);
        Assert.NotNull(inbound.CancelledAtUtc);
    }

    [Fact]
    public void Inbound_completion_requires_idempotency_key_and_creates_inventory_request()
    {
        var inbound = DomainWmsFactory.InspectionExemptInboundOrder();
        inbound.CreatePutawayTask("TASK-IN-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m);

        var exception = Assert.Throws<ArgumentException>(() => inbound.Complete(" "));
        var request = Assert.Single(inbound.Complete("idem-in-001"));

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        Assert.Equal("inbound", request.MovementType);
        Assert.Equal("idem-in-001", request.IdempotencyKey);
        Assert.Contains(inbound.GetDomainEvents(), x => x is InboundOrderCompletedDomainEvent);
    }

    [Fact]
    public void Quality_required_inbound_line_cannot_be_putaway_before_inspection_result()
    {
        var inbound = DomainWmsFactory.InboundOrder();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            inbound.CreatePutawayTask("TASK-IN-QUALITY-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m));

        Assert.Contains("quality", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quality_required_inbound_completion_waits_for_quality_result_and_posts_quality_stock()
    {
        var inbound = DomainWmsFactory.InboundOrder();

        var request = Assert.Single(inbound.Complete("idem-in-quality-001"));

        Assert.Equal(InboundOrderStatus.PendingQualityCheck, inbound.Status);
        Assert.Equal("quality", request.QualityStatus);
    }

    [Fact]
    public void Inspection_passed_releases_putaway_gate_after_pending_quality_check()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        inbound.Complete("idem-in-quality-001");

        inbound.ApplyInspectionResult("quality.InspectionPassed", "QI-001", "SKU-FG-1000", "LOT-001", null, 5m, "accepted");
        var task = inbound.CreatePutawayTask("TASK-IN-PASSED-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m);

        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        Assert.Equal("TASK-IN-PASSED-001", task.TaskNo);
    }

    [Fact]
    public void Inspection_rejected_keeps_putaway_blocked_and_creates_supplier_return_fact()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        inbound.Complete("idem-in-quality-001");

        var supplierReturn = inbound.ApplyInspectionResult("quality.InspectionRejected", "QI-001", "SKU-FG-1000", "LOT-001", null, 5m, "critical-defect");

        Assert.NotNull(supplierReturn);
        Assert.Equal("return-to-supplier", supplierReturn.DispositionType);
        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            inbound.CreatePutawayTask("TASK-IN-REJECTED-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m));
        Assert.Contains("rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conditional_release_allows_restricted_putaway_after_quality_result()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        inbound.Complete("idem-in-quality-001");

        inbound.ApplyInspectionResult("quality.InspectionConditionalReleased", "QI-001", "SKU-FG-1000", "LOT-001", null, 5m, "use-as-is");
        var task = inbound.CreatePutawayTask("TASK-IN-CONDITIONAL-001", "LINE-001", "LOC-STAGE", "LOC-RESTRICTED-01", 5m);

        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        Assert.Equal("LOC-RESTRICTED-01", task.ToLocationCode);
    }

    [Fact]
    public void Inspection_exempt_inbound_completion_keeps_direct_unrestricted_path()
    {
        var inbound = DomainWmsFactory.InspectionExemptInboundOrder();

        var request = Assert.Single(inbound.Complete("idem-in-exempt-001"));

        Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
        Assert.Equal("unrestricted", request.QualityStatus);
    }

    [Fact]
    public void Mixed_quality_order_allows_inspection_exempt_line_while_other_line_is_pending_quality()
    {
        var inbound = DomainWmsFactory.MixedQualityInboundOrder();

        var requests = inbound.Complete("idem-in-mixed-001");
        var exemptTask = inbound.CreatePutawayTask("TASK-IN-MIXED-001", "LINE-002", "LOC-STAGE", "LOC-A-01", 2m);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            inbound.CreatePutawayTask("TASK-IN-MIXED-002", "LINE-001", "LOC-STAGE", "LOC-A-02", 5m));

        Assert.Equal(InboundOrderStatus.PendingQualityCheck, inbound.Status);
        Assert.Contains(requests, x => x.SourceDocumentLineId == "LINE-001" && x.QualityStatus == "quality");
        Assert.Contains(requests, x => x.SourceDocumentLineId == "LINE-002" && x.QualityStatus == "unrestricted");
        Assert.Equal("LINE-002", exemptTask.SourceOrderLineNo);
        Assert.Contains("quality", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Qualified_quality_status_uses_direct_unrestricted_path_instead_of_pending_gate()
    {
        var inbound = DomainWmsFactory.QualifiedInboundOrder();

        var request = Assert.Single(inbound.Complete("idem-in-qualified-001"));
        inbound.MarkInventoryPostingFailed();
        var retry = Assert.Single(inbound.RetryInventoryPosting("idem-in-qualified-retry-001"));

        Assert.Equal("unrestricted", request.QualityStatus);
        Assert.Equal("unrestricted", retry.QualityStatus);
    }

    [Fact]
    public void Unknown_receiving_quality_status_fails_closed_to_pending_quality_gate()
    {
        var inbound = DomainWmsFactory.InboundOrderWithQualityStatus("iqc");

        var request = Assert.Single(inbound.Complete("idem-in-iqc-001"));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            inbound.CreatePutawayTask("TASK-IN-IQC-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m));

        Assert.Equal(InboundOrderStatus.PendingQualityCheck, inbound.Status);
        Assert.Equal("quality", request.QualityStatus);
        Assert.Contains("quality", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Putaway_quantity_cannot_exceed_inbound_line_quantity()
    {
        var inbound = DomainWmsFactory.InspectionExemptInboundOrder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            inbound.CreatePutawayTask("TASK-IN-002", "LINE-001", "LOC-STAGE", "LOC-A-01", 6m));

        Assert.Contains("putaway", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Completed_inbound_orders_are_immutable()
    {
        var inbound = DomainWmsFactory.InspectionExemptInboundOrder();
        inbound.CreatePutawayTask("TASK-IN-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m);
        inbound.Complete("idem-in-001");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            inbound.CreatePutawayTask("TASK-IN-002", "LINE-001", "LOC-STAGE", "LOC-B-01", 1m));

        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inbound_completion_rejects_empty_persisted_line_set()
    {
        var inbound = DomainWmsFactory.InboundOrder();
        DomainWmsFactory.ClearInboundLines(inbound);

        var exception = Assert.Throws<InvalidOperationException>(() => inbound.Complete("idem-in-001"));

        Assert.Contains("line", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outbound_pack_review_requires_idempotency_key_and_creates_inventory_request()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m);

        var exception = Assert.Throws<ArgumentException>(() => outbound.CompletePackReview("PACK-001", true, " "));
        var request = Assert.Single(outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OutboundOrderStatus.InventoryPostingPending, outbound.Status);
        Assert.Equal("outbound", request.MovementType);
        Assert.Equal("idem-out-001", request.IdempotencyKey);
        Assert.DoesNotContain(outbound.GetDomainEvents(), x => x is OutboundOrderCompletedDomainEvent);

        outbound.MarkInventoryPostingCompleted();

        Assert.Equal(OutboundOrderStatus.Completed, outbound.Status);
        Assert.NotNull(outbound.CompletedAtUtc);
        Assert.Contains(outbound.GetDomainEvents(), x => x is OutboundOrderCompletedDomainEvent);
    }

    [Fact]
    public void Outbound_pack_review_carries_inventory_reservation_id_to_movement_request()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m, "res-001");

        var request = Assert.Single(outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Equal("res-001", request.InventoryReservationId);
    }

    [Fact]
    public void Outbound_pack_review_rejects_empty_persisted_line_set()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        DomainWmsFactory.ClearOutboundLines(outbound);

        var exception = Assert.Throws<InvalidOperationException>(() => outbound.CompletePackReview("PACK-001", true, "idem-out-001"));

        Assert.Contains("line", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pick_quantity_cannot_exceed_outbound_line_quantity()
    {
        var outbound = DomainWmsFactory.OutboundOrder();

        var exception = Assert.Throws<KnownException>(() =>
            outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 5m));

        Assert.Contains("pick", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outbound_cancel_records_reason_and_raises_domain_event()
    {
        var outbound = DomainWmsFactory.OutboundOrder();
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 4m, "res-001");

        outbound.Cancel("customer-cancelled");

        Assert.Equal(OutboundOrderStatus.Cancelled, outbound.Status);
        Assert.Equal("customer-cancelled", outbound.CancellationReason);
        Assert.NotNull(outbound.CancelledAtUtc);
        Assert.Null(outbound.Lines.Single().InventoryReservationId);
        Assert.Contains(outbound.GetDomainEvents(), x => x is OutboundOrderCancelledDomainEvent);
    }

    [Fact]
    public void Wms_execution_aggregates_capture_nullable_operator_and_team_assignment_snapshots()
    {
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-ASSIGNED-001",
            "purchase-receipt",
            "PO-ASSIGNED-001",
            "SITE-01",
            [new InboundOrderLineDraft("10", "SKU-001", "pcs", 5m, "RECV-01", "LOT-001", "SER-001", "inspection-exempt", "company", null)],
            " user-emp-049 ",
            " TEAM-WH-01 ");
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-ASSIGNED-001",
            "sales-delivery",
            "SO-ASSIGNED-001",
            "SITE-01",
            [new OutboundOrderLineDraft("10", "SKU-001", "pcs", 5m, "BIN-01", "LOT-001", "SER-001", "qualified", "company", null)],
            "user-emp-049",
            "TEAM-WH-01");
        var count = CountExecution.Create(
            "org-001",
            "env-dev",
            "COUNT-ASSIGNED-001",
            "SKU-001",
            "pcs",
            "SITE-01",
            "BIN-01",
            5m,
            "user-emp-049",
            "TEAM-WH-01");

        Assert.Equal("user-emp-049", inbound.AssignedOperatorUserId);
        Assert.Equal("TEAM-WH-01", inbound.AssignedTeamId);
        Assert.Equal("user-emp-049", outbound.AssignedOperatorUserId);
        Assert.Equal("TEAM-WH-01", outbound.AssignedTeamId);
        Assert.Equal("user-emp-049", count.AssignedOperatorUserId);
        Assert.Equal("TEAM-WH-01", count.AssignedTeamId);
    }

    [Fact]
    public void Warehouse_tasks_capture_source_lot_serial_and_assignment_snapshots()
    {
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-TASK-001",
            "purchase-receipt",
            "PO-TASK-001",
            "SITE-01",
            [new InboundOrderLineDraft("10", "SKU-001", "pcs", 5m, "RECV-01", "LOT-IN-001", "SER-IN-001", "inspection-exempt", "company", null)]);
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-TASK-001",
            "sales-delivery",
            "SO-TASK-001",
            "SITE-01",
            [new OutboundOrderLineDraft("10", "SKU-001", "pcs", 5m, "BIN-01", "LOT-OUT-001", "SER-OUT-001", "qualified", "company", null)]);

        var putaway = inbound.CreatePutawayTask(
            "PUT-TASK-001",
            "10",
            "RECV-01",
            "BIN-01",
            5m,
            "user-emp-049",
            "TEAM-WH-01");
        var picking = outbound.CreatePickingTask(
            "PICK-TASK-001",
            "10",
            "BIN-01",
            "PACK-01",
            5m,
            assignedOperatorUserId: "user-emp-049",
            assignedTeamId: "TEAM-WH-01");

        Assert.Equal("LOT-IN-001", putaway.LotNo);
        Assert.Equal("SER-IN-001", putaway.SerialNo);
        Assert.Equal("LOT-OUT-001", picking.LotNo);
        Assert.Equal("SER-OUT-001", picking.SerialNo);
        Assert.Equal("user-emp-049", putaway.AssignedOperatorUserId);
        Assert.Equal("TEAM-WH-01", picking.AssignedTeamId);
    }

    [Fact]
    public void Warehouse_task_start_progress_and_full_completion_advance_version()
    {
        var task = WarehouseTask.CreatePutaway(
            "org-001",
            "env-dev",
            "PUT-LIFECYCLE-001",
            "IN-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-01",
            "RECV-01",
            "BIN-01",
            5m);

        task.Start("user-emp-049", 1);
        task.RecordProgress(3m, "user-emp-049", 2);
        task.Complete(5m, "user-emp-049", null, 3);

        Assert.Equal(WarehouseTaskStatus.Completed, task.Status);
        Assert.Equal(5m, task.ExecutedQuantity);
        Assert.Equal(4, task.Version);
        Assert.NotNull(task.StartedAtUtc);
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Equal("user-emp-049", task.CompletedBy);
        Assert.Null(task.CompletionReason);
    }

    [Fact]
    public void Picking_task_short_completion_requires_reason_and_records_difference_terminal()
    {
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "PICK-DIFFERENCE-001",
            "OUT-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-01",
            "BIN-01",
            "PACK-01",
            5m);
        task.Start("user-emp-049", 1);
        task.RecordProgress(3m, "user-emp-049", 2);

        var missingReason = Assert.Throws<ArgumentException>(() =>
            task.Complete(3m, "user-emp-049", " ", 3));
        task.Complete(3m, "user-emp-049", "source-bin-shortage", 3);
        var terminalRewrite = Assert.Throws<InvalidOperationException>(() =>
            task.RecordProgress(4m, "user-emp-049", 4));

        Assert.Contains("reason", missingReason.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WarehouseTaskStatus.CompletedWithDifference, task.Status);
        Assert.Equal("source-bin-shortage", task.CompletionReason);
        Assert.Equal(4, task.Version);
        Assert.Contains("terminal", terminalRewrite.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Putaway_task_rejects_short_completion_without_mutating_version()
    {
        var task = WarehouseTask.CreatePutaway(
            "org-001",
            "env-dev",
            "PUT-SHORT-001",
            "IN-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-01",
            "RECV-01",
            "BIN-01",
            5m);
        task.Start("user-emp-049", 1);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            task.Complete(4m, "user-emp-049", "short", 2));

        Assert.Contains("full", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WarehouseTaskStatus.InProgress, task.Status);
        Assert.Equal(2, task.Version);
        Assert.Equal(0m, task.ExecutedQuantity);
    }

    [Fact]
    public void Warehouse_task_exception_is_a_blocking_terminal_with_audited_facts()
    {
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "PICK-EXCEPTION-001",
            "OUT-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-01",
            "BIN-01",
            "PACK-01",
            5m);

        task.ReportException("BIN-BLOCKED", "source bin is blocked", "user-emp-049", 1);
        var completion = Assert.Throws<InvalidOperationException>(() =>
            task.Complete(5m, "user-emp-049", null, 2));

        Assert.Equal(WarehouseTaskStatus.Exception, task.Status);
        Assert.Equal("BIN-BLOCKED", task.ExceptionCode);
        Assert.Equal("source bin is blocked", task.ExceptionReason);
        Assert.Equal("user-emp-049", task.ExceptionBy);
        Assert.NotNull(task.ExceptionAtUtc);
        Assert.Equal(2, task.Version);
        Assert.Contains("terminal", completion.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Warehouse_task_rejects_stale_expected_version_before_mutation()
    {
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "PICK-VERSION-001",
            "OUT-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-01",
            "BIN-01",
            "PACK-01",
            5m);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            task.Start("user-emp-049", 2));

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WarehouseTaskStatus.Open, task.Status);
        Assert.Equal(1, task.Version);
        Assert.Null(task.StartedAtUtc);
    }

    [Fact]
    public void Warehouse_task_tracks_execution_bounds()
    {
        var task = WarehouseTask.CreatePutaway(
            "org-001",
            "env-dev",
            "TASK-IN-001",
            "IN-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-STAGE",
            "LOC-A-01",
            5m);

        task.RecordProgress(3m);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => task.RecordProgress(6m));

        Assert.Equal(3m, task.ExecutedQuantity);
        Assert.Contains("executed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_wcs_progress_keeps_partial_task_open_until_its_completion_callback_finishes_quantity()
    {
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-WCS-001",
            "OUT-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            4m);

        task.RecordProgress(3m);

        Assert.Equal(WarehouseTaskStatus.Open, task.Status);
        Assert.Equal(3m, task.ExecutedQuantity);
        Assert.Equal(2, task.Version);
    }

    [Fact]
    public void Warehouse_task_progress_is_monotonic_and_completed_tasks_are_locked()
    {
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-OUT-001",
            "OUT-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            4m);

        task.RecordProgress(3m);
        var regression = Assert.Throws<InvalidOperationException>(() => task.RecordProgress(2m));
        task.RecordProgress(4m);
        var completedRewrite = Assert.Throws<InvalidOperationException>(() => task.RecordProgress(4m));

        Assert.Equal(4m, task.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.Completed, task.Status);
        Assert.Contains("regress", regression.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("completed", completedRewrite.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outbound_pack_review_posts_actual_picked_quantity_and_records_backorder()
    {
        var outbound = DomainWmsFactory.OutboundOrder(requestedQuantity: 10m);
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 10m, "res-001");

        var request = Assert.Single(outbound.CompletePackReview(
            "PACK-001",
            true,
            "idem-out-001",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["LINE-001"] = 8m,
            }));

        var line = outbound.Lines.Single();
        Assert.Equal(8m, request.Quantity);
        Assert.Equal(8m, line.IssuedQuantity);
        Assert.Equal(2m, line.BackorderQuantity);
    }

    [Fact]
    public void Outbound_pack_review_clamps_cumulative_pick_execution_to_requested_quantity()
    {
        var outbound = DomainWmsFactory.OutboundOrder(requestedQuantity: 10m);
        outbound.CreatePickingTask("TASK-OUT-001", "LINE-001", "LOC-A-01", "PACK-01", 7m, "res-001");
        outbound.CreatePickingTask("TASK-OUT-002", "LINE-001", "LOC-A-01", "PACK-01", 6m, "res-001");

        var request = Assert.Single(outbound.CompletePackReview(
            "PACK-001",
            true,
            "idem-out-001",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["LINE-001"] = 13m,
            }));

        var line = outbound.Lines.Single();
        Assert.Equal(10m, request.Quantity);
        Assert.Equal(10m, line.IssuedQuantity);
        Assert.Equal(0m, line.BackorderQuantity);
    }
}

internal static class DomainWmsFactory
{
    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder InboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")]);
    }

    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder InspectionExemptInboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "inspection-exempt", "company", "owner-001")]);
    }

    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder MixedQualityInboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [
                new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001"),
                new InboundOrderLineDraft("LINE-002", "SKU-FG-2000", "kg", 2m, "LOC-STAGE", "LOT-002", null, "inspection-exempt", "company", "owner-001")
            ]);
    }

    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder QualifiedInboundOrder()
    {
        return InboundOrderWithQualityStatus("qualified");
    }

    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder InboundOrderWithQualityStatus(string qualityStatus)
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, qualityStatus, "company", "owner-001")]);
    }

    public static Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder OutboundOrder(decimal requestedQuantity = 4m)
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", requestedQuantity, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
    }

    public static InventoryMovementRequest MovementRequest()
    {
        return InventoryMovementRequest.Create(
            "org-001",
            "env-dev",
            "inbound",
            "IN-001",
            "LINE-001",
            "idem-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            5m);
    }

    public static void ClearInboundLines(Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder inbound)
    {
        var lines = (List<InboundOrderLine>)typeof(Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder)
            .GetField("lines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(inbound)!;
        lines.Clear();
    }

    public static void ClearOutboundLines(Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder outbound)
    {
        var lines = (List<OutboundOrderLine>)typeof(Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder)
            .GetField("lines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(outbound)!;
        lines.Clear();
    }
}
