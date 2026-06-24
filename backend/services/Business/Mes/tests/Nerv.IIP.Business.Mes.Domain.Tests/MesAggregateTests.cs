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
            "LOT-FG-001",
            "SN-FG-001");

        Assert.Equal("WO-001", request.WorkOrderId);
        Assert.Equal("FGR-001", request.RequestNo);
        Assert.Equal("SKU-001", request.SkuId);
        Assert.Equal(9m, request.Quantity);
        Assert.Equal("PCS", request.UomCode);
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
    public void WorkOrder_allows_scrap_without_consuming_good_quantity_target()
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

        workOrder.RecordProductionProgress(95m, 10m, DateTimeOffset.Parse("2026-05-23T09:00:00Z"));

        Assert.Equal(WorkOrder.StartedStatus, workOrder.Status);
        Assert.Equal(95m, workOrder.CompletedQuantity);
        Assert.Equal(10m, workOrder.ScrapQuantity);

        workOrder.RecordProductionProgress(5m, 0m, DateTimeOffset.Parse("2026-05-23T10:00:00Z"));

        Assert.Equal(WorkOrder.CompletedStatus, workOrder.Status);
        Assert.Equal(100m, workOrder.CompletedQuantity);
        Assert.Equal(10m, workOrder.ScrapQuantity);
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
