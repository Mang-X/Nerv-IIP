using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

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
            DateTimeOffset.Parse("2026-05-23T09:00:00Z"));

        Assert.Equal(9m, report.GoodQuantity);
        Assert.Equal("PRPT-001", report.ReportNo);
        Assert.Equal(1m, report.ScrapQuantity);
        Assert.True(report.CompletesOperation);
    }

    [Fact]
    public void FinishedGoodsReceiptRequest_references_work_order_sku_quantity_and_uom_only()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-001",
            9m,
            "PCS",
            DateTimeOffset.Parse("2026-05-23T09:30:00Z"));

        Assert.Equal("WO-001", request.WorkOrderId);
        Assert.Equal("FGR-001", request.RequestNo);
        Assert.Equal("SKU-001", request.SkuId);
        Assert.Equal(9m, request.Quantity);
        Assert.Equal("PCS", request.UomCode);
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
