using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class MesAggregateTests
{
    [Fact]
    public void WorkOrder_references_ProductEngineering_production_version_by_public_id()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-001",
            "production-version-from-issue-95",
            10m,
            100,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        Assert.Equal("production-version-from-issue-95", workOrder.ProductionVersionId);
        Assert.Equal("SKU-001", workOrder.SkuId);
    }

    [Fact]
    public void WorkOrder_release_creates_operation_tasks_from_routing_step_snapshots()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-002",
            "SKU-001",
            "PV-001",
            5m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        var tasks = workOrder.Release(
            DateTimeOffset.Parse("2026-05-23T08:00:00Z"),
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-A", ["WC-B"], TimeSpan.FromMinutes(30)),
                new RoutingStepSnapshot("OP-20", 20, "WC-C", [], TimeSpan.FromMinutes(45)),
            ]);

        Assert.Collection(
            tasks,
            first =>
            {
                Assert.Equal("WO-002", first.WorkOrderId);
                Assert.Equal("OP-10", first.OperationTaskId);
                Assert.Equal(10, first.OperationSequence);
                Assert.Equal(OperationTaskLifecycleStatus.Queued, first.Status);
            },
            second => Assert.Equal("OP-20", second.OperationTaskId));
    }

    [Fact]
    public void Rule_schedule_result_is_deterministic_for_same_assignments()
    {
        var scheduledAt = DateTimeOffset.Parse("2026-05-23T08:00:00Z");
        var assignments = new[]
        {
            new ScheduledOperationSnapshot("WO-001", "OP-10", "WC-A", scheduledAt, scheduledAt.AddMinutes(30), "rule-sequenced"),
        };

        var first = ScheduleResult.Create(1, ScheduleTrigger.Manual, scheduledAt, assignments, []);
        var second = ScheduleResult.Create(1, ScheduleTrigger.Manual, scheduledAt, assignments, []);

        Assert.Equal(first.AssignmentsJson, second.AssignmentsJson);
        Assert.Contains("\"_v\":1", first.AssignmentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReport_records_quantities_and_operation_completion()
    {
        var report = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-001",
            "WO-001",
            "OP-10",
            9m,
            1m,
            true,
            DateTimeOffset.Parse("2026-05-23T09:00:00Z"),
            reworkQuantity: 2m,
            scrapReasonCode: "SCRAP-SURFACE",
            defectRecordNo: "DEF-001",
            producedLotNo: "LOT-FG-001",
            serialNo: "SN-FG-001");

        Assert.Equal(9m, report.GoodQuantity);
        Assert.Equal("PRPT-001", report.ReportNo);
        Assert.Equal(1m, report.ScrapQuantity);
        Assert.Equal(2m, report.ReworkQuantity);
        Assert.Equal("SCRAP-SURFACE", report.ScrapReasonCode);
        Assert.Equal("DEF-001", report.DefectRecordNo);
        Assert.Equal("LOT-FG-001", report.ProducedLotNo);
        Assert.Equal("SN-FG-001", report.SerialNo);
        Assert.True(report.CompletesOperation);
        Assert.IsType<ProductionReportRecordedDomainEvent>(report.GetDomainEvents().Single());
    }

    [Fact]
    public void FinishedGoodsReceiptRequest_references_work_order_sku_quantity_uom_and_genealogy()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-001",
            9m,
            "PCS",
            DateTimeOffset.Parse("2026-05-23T09:30:00Z"),
            producedLotNo: "LOT-FG-001",
            serialNo: "SN-FG-001",
            unitCost: 12.34m);

        Assert.Equal("WO-001", request.WorkOrderId);
        Assert.Equal("FGR-001", request.RequestNo);
        Assert.Equal("SKU-001", request.SkuId);
        Assert.Equal(9m, request.Quantity);
        Assert.Equal("PCS", request.UomCode);
        Assert.Equal(12.34m, request.UnitCost);
        Assert.Equal("LOT-FG-001", request.ProducedLotNo);
        Assert.Equal("SN-FG-001", request.SerialNo);
        Assert.Equal(FinishedGoodsReceiptRequest.RequestedStatus, request.Status);
        Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());
    }

    [Fact]
    public void ProductionReport_rejects_negative_or_empty_quantities()
    {
        var reportedAt = DateTimeOffset.Parse("2026-05-23T09:00:00Z");

        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-001",
            "WO-001",
            "OP-10",
            -1m,
            0m,
            false,
            reportedAt));

        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-002",
            "WO-001",
            "OP-10",
            0m,
            0m,
            false,
            reportedAt));
    }

    [Fact]
    public void Aggregates_reject_blank_organization_id()
    {
        var dueUtc = DateTimeOffset.Parse("2026-05-23T10:00:00Z");

        Assert.Throws<ArgumentException>(() => WorkOrder.Create("", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 10, dueUtc));
        Assert.Throws<ArgumentException>(() => OperationTask.Queue("", "env-dev", "WO-001", "OP-10", 10, "WC-A", [], dueUtc, TimeSpan.FromMinutes(30)));
        Assert.Throws<ArgumentException>(() => ProductionReport.Record("", "env-dev", "PRPT-001", "WO-001", "OP-10", 1m, 0m, true, dueUtc));
        Assert.Throws<ArgumentException>(() => FinishedGoodsReceiptRequest.Create("", "env-dev", "FGR-001", "WO-001", "SKU-001", 1m, "PCS", dueUtc));
    }

    [Fact]
    public void WorkOrder_cannot_be_released_twice()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-003",
            "SKU-001",
            "PV-001",
            5m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"));
        var routingSteps = new[]
        {
            new RoutingStepSnapshot("OP-10", 10, "WC-A", [], TimeSpan.FromMinutes(30)),
        };

        _ = workOrder.Release(DateTimeOffset.Parse("2026-05-23T08:00:00Z"), routingSteps);

        Assert.Throws<InvalidOperationException>(() =>
            workOrder.Release(DateTimeOffset.Parse("2026-05-23T08:00:00Z"), routingSteps));
    }

    [Theory]
    [InlineData(OperationTaskLifecycleStatus.InProgress)]
    [InlineData(OperationTaskLifecycleStatus.Paused)]
    public void OperationTask_rejects_schedule_assignment_for_active_tasks_as_known_business_error(
        OperationTaskLifecycleStatus status)
    {
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "OP-10",
            status,
            10,
            "WC-OLD",
            [],
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse("2026-06-01T08:05:00Z"),
            null);

        var exception = Assert.Throws<KnownException>(() => task.ApplyScheduleAssignment(
            "WC-OIL",
            "DEV-OIL-01",
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-01T13:30:00Z"),
            DateTimeOffset.Parse("2026-06-01T07:30:00Z")));

        Assert.Contains(status.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkOrder_tracks_started_completed_and_closed_progress()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-PROGRESS",
            "SKU-001",
            "PV-001",
            10m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"),
            overReceiptTolerancePercent: 10m);

        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-05-23T08:00:00Z"));
        workOrder.RecordProductionProgress(6m, 1m, DateTimeOffset.Parse("2026-05-23T09:00:00Z"));

        Assert.Equal(WorkOrder.StartedStatus, workOrder.Status);
        Assert.Equal(6m, workOrder.CompletedQuantity);
        Assert.Equal(1m, workOrder.ScrapQuantity);

        workOrder.RecordProductionProgress(4m, 0m, DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        Assert.Equal(WorkOrder.CompletedStatus, workOrder.Status);
        workOrder.Close(DateTimeOffset.Parse("2026-05-23T11:00:00Z"));
        Assert.Equal(WorkOrder.ClosedStatus, workOrder.Status);
        Assert.NotNull(workOrder.ClosedAtUtc);
    }

    [Fact]
    public void WorkOrder_rejects_progress_beyond_tolerance()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-OVER",
            "SKU-001",
            "PV-001",
            10m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"),
            overReceiptTolerancePercent: 0m);
        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-05-23T08:00:00Z"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workOrder.RecordProductionProgress(11m, 0m, DateTimeOffset.Parse("2026-05-23T09:00:00Z")));

        Assert.Contains("tolerance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkOrder_rejects_good_plus_scrap_beyond_overreceipt_tolerance()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-SCRAP",
            "SKU-001",
            "PV-001",
            100m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"),
            overReceiptTolerancePercent: 0m);
        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-05-23T08:00:00Z"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workOrder.RecordProductionProgress(95m, 10m, DateTimeOffset.Parse("2026-05-23T09:00:00Z")));

        Assert.Contains("tolerance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkOrder_emits_completed_and_closed_domain_events()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-COMPLETE-EVENT",
            "SKU-001",
            "PV-001",
            10m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"));
        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-05-23T08:00:00Z"));
        workOrder.ClearDomainEvents();

        workOrder.RecordProductionProgress(10m, 0m, DateTimeOffset.Parse("2026-05-23T09:00:00Z"));
        workOrder.Close(DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        var eventNames = workOrder.GetDomainEvents().Select(x => x.GetType().Name).ToArray();
        Assert.Contains("WorkOrderCompletedDomainEvent", eventNames);
        Assert.Contains("WorkOrderClosedDomainEvent", eventNames);
    }

    [Fact]
    public void MaterialIssueRequest_creation_tracks_requested_status_without_inventory_movement_event()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            3m,
            DateTimeOffset.Parse("2026-05-23T08:10:00Z"));

        Assert.Equal(MaterialIssueRequest.RequestedStatus, request.Status);
        Assert.Empty(request.GetDomainEvents());
    }

    [Fact]
    public void MaterialIssueRequest_line_side_receipt_raises_transfer_events_with_delta_quantity()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            3m,
            DateTimeOffset.Parse("2026-05-23T08:10:00Z"));
        request.ClearDomainEvents();

        request.ConfirmLineSideReceipt(
            DateTimeOffset.Parse("2026-05-23T08:30:00Z"),
            2m,
            "LOT-001");

        var events = request.GetDomainEvents().ToArray();
        var issueEvent = Assert.IsType<MaterialIssueRequestedDomainEvent>(events[0]);
        var receiptEvent = Assert.IsType<MaterialLineSideReceiptConfirmedDomainEvent>(events[1]);
        Assert.Same(request, issueEvent.MaterialIssueRequest);
        Assert.Equal(2m, issueEvent.IssuedQuantity);
        Assert.Same(request, receiptEvent.MaterialIssueRequest);
        Assert.Equal(2m, receiptEvent.ReceivedQuantity);
    }

    [Fact]
    public void MaterialIssueRequest_cancel_returns_received_material_that_has_no_lot()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-002",
            "WO-002",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            DateTimeOffset.Parse("2026-05-23T08:10:00Z"));
        // A line-side receipt may be confirmed without a material lot.
        request.ConfirmLineSideReceipt(DateTimeOffset.Parse("2026-05-23T08:30:00Z"), 5m);
        request.ClearDomainEvents();

        // Cancelling the work order must not throw even though the received material carries no lot
        // (previously threw InvalidOperationException -> escaped as HTTP 500 from the cancel path).
        var exception = Record.Exception(
            () => request.CancelForWorkOrderCancellation(DateTimeOffset.Parse("2026-05-23T09:00:00Z")));
        Assert.Null(exception);

        Assert.Equal(MaterialIssueRequest.ReturnRequestedStatus, request.Status);
        var events = request.GetDomainEvents().ToArray();
        Assert.Contains(events, e => e is MaterialLineSideReturnRequestedDomainEvent);
        var toWarehouse = Assert.IsType<MaterialReturnedToWarehouseDomainEvent>(
            events.Single(e => e is MaterialReturnedToWarehouseDomainEvent));
        Assert.Equal(5m, toWarehouse.ReturnedQuantity);
        Assert.Null(toWarehouse.MaterialLotId);
    }

    [Fact]
    public void DefectRecord_tracks_ncr_request_and_disposition()
    {
        var defect = DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-001",
            "WO-001",
            "OP-10",
            "SURFACE",
            1m,
            DateTimeOffset.Parse("2026-05-23T09:20:00Z"));

        Assert.Equal(DefectRecord.OpenStatus, defect.Status);
        Assert.IsType<DefectRaisedDomainEvent>(defect.GetDomainEvents().Single());

        defect.AcceptDisposition("NCR-001", "NCR-2026-001", "Rework", "RW-WO-001", DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        Assert.Equal(DefectRecord.ReworkPendingStatus, defect.Status);
        Assert.Equal("NCR-001", defect.NcrId);
        Assert.Equal("NCR-2026-001", defect.NcrCode);
        Assert.Equal("Rework", defect.DispositionType);
        Assert.Equal("RW-WO-001", defect.DispositionReferenceId);
    }

    [Theory]
    [InlineData("conditional-release")]
    [InlineData("sort-and-screen")]
    public void DefectRecord_explicitly_accepts_quality_dispositions_without_mes_specific_state(string dispositionType)
    {
        var defect = DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-001",
            "WO-001",
            "OP-10",
            "SURFACE",
            1m,
            DateTimeOffset.Parse("2026-05-23T09:20:00Z"));

        defect.AcceptDisposition("NCR-001", "NCR-2026-001", dispositionType, null, DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        Assert.Equal(DefectRecord.DispositionAcceptedStatus, defect.Status);
        Assert.Equal(dispositionType, defect.DispositionType);
        Assert.Null(defect.DispositionReferenceId);
    }

    [Fact]
    public void QualityHoldContext_ignores_stale_inspection_results()
    {
        var rejectedAt = DateTimeOffset.Parse("2026-06-19T09:00:00Z");
        var context = QualityHoldContext.Capture(
            "org-001",
            "env-dev",
            "WO-QUALITY",
            "OP-10",
            "business-mes",
            "WO-QUALITY",
            "QIR-REJECTED",
            "QIP-001",
            "rejected",
            "quality.InspectionRejected",
            "surface defect",
            rejectedAt);

        context.ApplyInspectionResult(
            "QIR-PASSED",
            "QIP-001",
            "passed",
            "quality.InspectionPassed",
            null,
            rejectedAt.AddMinutes(-5));

        Assert.True(context.Active);
        Assert.Equal("QIR-REJECTED", context.InspectionRecordId);
        Assert.Equal("rejected", context.Result);
        Assert.Equal(rejectedAt, context.RecordedAtUtc);
    }

    [Fact]
    public void QualityHoldContext_records_hold_and_release_audit_without_losing_original_hold_source()
    {
        var rejectedAt = DateTimeOffset.Parse("2026-07-05T09:00:00Z");
        var releasedAt = rejectedAt.AddMinutes(30);
        var context = QualityHoldContext.Capture(
            "org-001",
            "env-dev",
            "WO-QUALITY",
            "OP-10",
            "business-mes",
            "OP-10",
            "QIR-REJECTED",
            "QIP-001",
            "rejected",
            "quality.InspectionRejected",
            "surface defect",
            rejectedAt,
            "quality");

        context.ApplyInspectionResult(
            "QIR-CONDITIONAL",
            "QIP-001",
            "conditional-release",
            "quality.InspectionConditionalReleased",
            "released for OP-10 only",
            releasedAt,
            "quality");

        Assert.False(context.Active);
        Assert.Equal("QIR-REJECTED", context.HeldInspectionRecordId);
        Assert.Equal("surface defect", context.HoldReason);
        Assert.Equal(rejectedAt, context.HeldAtUtc);
        Assert.Equal("quality", context.HeldBy);
        Assert.Equal("QIR-CONDITIONAL", context.ReleaseInspectionRecordId);
        Assert.Equal("released for OP-10 only", context.ReleaseReason);
        Assert.Equal(releasedAt, context.ReleasedAtUtc);
        Assert.Equal("quality", context.ReleasedBy);
        Assert.Equal("quality.InspectionConditionalReleased", context.ReleaseSource);
    }

    [Fact]
    public void QualityHoldContext_clears_previous_release_audit_when_reopened_by_later_rejection()
    {
        var rejectedAt = DateTimeOffset.Parse("2026-07-05T09:00:00Z");
        var releasedAt = rejectedAt.AddMinutes(30);
        var reopenedAt = rejectedAt.AddMinutes(45);
        var context = QualityHoldContext.Capture(
            "org-001",
            "env-dev",
            "WO-QUALITY",
            "OP-10",
            "business-mes",
            "OP-10",
            "QIR-REJECTED-1",
            "QIP-001",
            "rejected",
            "quality.InspectionRejected",
            "surface defect",
            rejectedAt,
            "quality");

        context.ApplyInspectionResult(
            "QIR-CONDITIONAL",
            "QIP-001",
            "conditional-release",
            "quality.InspectionConditionalReleased",
            "released for OP-10 only",
            releasedAt,
            "quality");
        context.ApplyInspectionResult(
            "QIR-REJECTED-2",
            "QIP-001",
            "rejected",
            "quality.InspectionRejected",
            "recheck failed",
            reopenedAt,
            "quality");

        Assert.True(context.Active);
        Assert.Equal("QIR-REJECTED-2", context.HeldInspectionRecordId);
        Assert.Equal("recheck failed", context.HoldReason);
        Assert.Equal(reopenedAt, context.HeldAtUtc);
        Assert.Null(context.ReleaseInspectionRecordId);
        Assert.Null(context.ReleaseReason);
        Assert.Null(context.ReleasedAtUtc);
        Assert.Null(context.ReleasedBy);
        Assert.Null(context.ReleaseSource);
    }

    [Fact]
    public void QualityHoldContext_force_release_is_idempotent_and_requires_existing_active_hold()
    {
        var rejectedAt = DateTimeOffset.Parse("2026-07-05T09:00:00Z");
        var firstReleaseAt = rejectedAt.AddMinutes(10);
        var secondReleaseAt = rejectedAt.AddMinutes(20);
        var context = QualityHoldContext.Capture(
            "org-001",
            "env-dev",
            "WO-QUALITY",
            null,
            "business-mes",
            "WO-QUALITY",
            "QIR-REJECTED",
            "QIP-001",
            "rejected",
            "quality.InspectionRejected",
            "surface defect",
            rejectedAt,
            "quality");

        context.ForceRelease("approved after QA recheck", "supervisor-001", firstReleaseAt);
        context.ForceRelease("second release should not overwrite audit", "supervisor-002", secondReleaseAt);

        Assert.False(context.Active);
        Assert.Equal("manual-force-release", context.ReleaseSource);
        Assert.Equal("approved after QA recheck", context.ReleaseReason);
        Assert.Equal("supervisor-001", context.ReleasedBy);
        Assert.Equal(firstReleaseAt, context.ReleasedAtUtc);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public void WorkOrder_mark_released_rejects_closed_states(string closedStatus)
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-CLOSED",
            "SKU-001",
            "PV-001",
            5m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        typeof(WorkOrder)
            .GetProperty(nameof(WorkOrder.Status))!
            .SetValue(workOrder, closedStatus);

        var exception = Assert.Throws<InvalidOperationException>(() => workOrder.MarkReleased());
        Assert.Contains("closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
