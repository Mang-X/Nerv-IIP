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
    public void Rework_work_order_captures_quality_source_facts_and_creation_event()
    {
        var requestedAtUtc = DateTimeOffset.Parse("2026-08-29T08:00:00Z");

        var workOrder = WorkOrder.CreateRework(
            "org-001",
            "env-dev",
            "WO-RW-001",
            "SKU-001",
            "PV-001",
            "PCS",
            3m,
            100,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            "WO-SOURCE-001",
            "OP-SOURCE-10",
            "DEF-001",
            "ncr-001",
            "NCR-2026-0001",
            "LOT-001",
            "SN-001",
            requestedAtUtc,
            "corr-001",
            "evt-rework-requested-001");

        Assert.Equal(WorkOrder.ReworkType, workOrder.WorkOrderType);
        Assert.Equal("WO-SOURCE-001", workOrder.SourceWorkOrderId);
        Assert.Equal("OP-SOURCE-10", workOrder.SourceOperationTaskId);
        Assert.Equal("DEF-001", workOrder.SourceDefectNo);
        Assert.Equal("ncr-001", workOrder.SourceNcrId);
        Assert.Equal("NCR-2026-0001", workOrder.SourceNcrCode);
        Assert.Equal("LOT-001", workOrder.SourceLotNo);
        Assert.Equal("SN-001", workOrder.SourceSerialNo);
        Assert.Equal(requestedAtUtc, workOrder.SourceReworkRequestedAtUtc);
        var created = Assert.IsType<ReworkWorkOrderCreatedDomainEvent>(Assert.Single(workOrder.GetDomainEvents()));
        Assert.Same(workOrder, created.WorkOrder);
        Assert.Equal(requestedAtUtc, created.RequestedAtUtc);
        Assert.Equal("corr-001", created.CorrelationId);
        Assert.Equal("evt-rework-requested-001", created.CausationId);
    }

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
        Assert.Equal(WorkOrder.StandardType, workOrder.WorkOrderType);
        Assert.Null(workOrder.SourceNcrId);
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

        var earliestStartUtc = DateTimeOffset.Parse("2026-05-23T08:00:00Z");
        var tasks = workOrder.Release(
            earliestStartUtc,
            WorkOrderReleaseFactTime.NotLaterThan(earliestStartUtc, null),
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-A", ["WC-B"], TimeSpan.FromMinutes(30)),
                new RoutingStepSnapshot("OP-20", 20, "WC-C", [], TimeSpan.FromMinutes(45)),
            ]);

        // 本用例里两个入参恰好同值，只钉住「事件带走的是调用方交出的发布事实时刻」这一点；
        // 「两者不可一值两用」由下一条用例分开钉。
        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        Assert.Equal(earliestStartUtc, domainEvent.ReleasedAt.Value);
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

    /// <summary>
    /// <c>earliestStartUtc</c>（排产用的最早可开工时刻，**允许落在未来**：下达后下一班开工是正常排产）
    /// 与发布事实的时刻是两件事。若把前者当后者用，该工单工序此后的每一条报工都会被 Quality 的
    /// <c>PeriodicInspectionOperation</c> 判为「报工早于发布」抛出、整封进死信——正是 #3117 修的那个缺陷
    /// 换了个入口原样重演。本用例把两者拉开：工序拿到未来的可开工时刻，发布事实拿到过去的发布时刻。
    /// </summary>
    [Fact]
    public void WorkOrder_release_does_not_reuse_the_earliest_start_as_the_release_fact_time()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-3117-SPLIT",
            "SKU-001",
            "PV-001",
            5m,
            10,
            DateTimeOffset.Parse("2026-05-30T10:00:00Z"));
        var releasedAtUtc = DateTimeOffset.Parse("2026-05-23T08:00:00Z");
        var earliestStartUtc = releasedAtUtc.AddDays(3);

        var tasks = workOrder.Release(
            earliestStartUtc,
            WorkOrderReleaseFactTime.NotLaterThan(releasedAtUtc, null),
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-A", [], TimeSpan.FromMinutes(30)),
            ]);

        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        Assert.Equal(releasedAtUtc, domainEvent.ReleasedAt.Value);
        Assert.Equal(earliestStartUtc, Assert.Single(tasks).EarliestStartUtc);
    }

    /// <summary>
    /// 不携带工序的那条发布：事件时刻取**调用当刻**的 <c>CreatedAtUtc</c>，不取墙钟。
    /// 唯一生产调用方（<c>WorldHistorySeedService</c>）先把 <c>CreatedAtUtc</c> 回拨到历史创建时刻再调它，
    /// 顺序反了事件带走的就是播种当下的 <c>UtcNow</c>——既非历史创建时刻，也晚于该工单全部历史报工。
    /// </summary>
    [Fact]
    public void WorkOrder_mark_released_without_operations_dates_the_release_fact_at_the_creation_moment()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-3117-NO-OPS",
            "SKU-001",
            "PV-001",
            5m,
            10,
            DateTimeOffset.Parse("2026-05-30T10:00:00Z"));
        workOrder.ClearDomainEvents();

        workOrder.MarkReleased();

        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        Assert.Equal(workOrder.CreatedAtUtc, domainEvent.ReleasedAt.Value);
        Assert.Empty(domainEvent.OperationTasks);
    }

    /// <summary>
    /// 报工下界：取「更早者」，不是「有报工就取报工」。第三条钉住**恰好相等**这个边界
    /// （<c>&lt;</c> 变 <c>&lt;=</c> 时取值不变，但它是唯一能把「严格早于」与「不晚于」区分开的输入）。
    /// </summary>
    [Theory]
    [InlineData("2026-06-01T10:00:00Z", null, "2026-06-01T10:00:00Z")]
    [InlineData("2026-06-01T10:00:00Z", "2026-06-01T06:00:00Z", "2026-06-01T06:00:00Z")]
    [InlineData("2026-06-01T10:00:00Z", "2026-06-01T10:00:00Z", "2026-06-01T10:00:00Z")]
    [InlineData("2026-06-01T10:00:00Z", "2026-06-01T14:00:00Z", "2026-06-01T10:00:00Z")]
    public void WorkOrderReleaseFactTime_takes_the_earlier_of_the_candidate_and_the_earliest_report(
        string candidate,
        string? earliestReport,
        string expected)
    {
        var factTime = WorkOrderReleaseFactTime.NotLaterThan(
            DateTimeOffset.Parse(candidate),
            earliestReport is null ? null : DateTimeOffset.Parse(earliestReport));

        Assert.Equal(DateTimeOffset.Parse(expected), factTime.Value);
    }

    /// <summary>
    /// 信任边界上的那一夹：外部给来的候选落在未来时压到当前时刻，不在未来时原样通过。
    /// 未来值会让该工序此后的每一条报工进死信；原样通过那一半同样要钉，
    /// 否则「一律取当前时刻」这个变异不可分辨，#3117 本身就会被这一夹撤销。
    /// </summary>
    [Theory]
    [InlineData("2026-06-01T14:00:00Z", "2026-06-01T10:00:00Z", "2026-06-01T10:00:00Z")]
    [InlineData("2026-06-01T06:00:00Z", "2026-06-01T10:00:00Z", "2026-06-01T06:00:00Z")]
    [InlineData("2026-06-01T10:00:00Z", "2026-06-01T10:00:00Z", "2026-06-01T10:00:00Z")]
    public void WorkOrderReleaseFactTime_clamps_an_untrusted_candidate_to_now(
        string candidate,
        string now,
        string expected)
    {
        var clamped = WorkOrderReleaseFactTime.UntrustedCandidate(
            DateTimeOffset.Parse(candidate),
            DateTimeOffset.Parse(now));

        Assert.Equal(DateTimeOffset.Parse(expected), clamped);
    }

    [Fact]
    public void WorkOrder_mark_released_rejects_empty_operation_tasks_without_changing_state_or_publishing_event()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-2095-EMPTY",
            "SKU-2095",
            "PV-2095",
            12m,
            1,
            DateTimeOffset.Parse("2026-08-24T08:00:00Z"));
        workOrder.ClearDomainEvents();

        Assert.Throws<ArgumentException>(() => workOrder.MarkReleased([], WorkOrderReleaseFactTime.NotLaterThan(DateTimeOffset.Parse("2026-08-24T08:00:00Z"), null)));

        Assert.Equal(WorkOrder.CreatedStatus, workOrder.Status);
        Assert.DoesNotContain(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent);
    }

    [Fact]
    public void WorkOrder_mark_released_with_operation_tasks_changes_state_and_publishes_supplied_tasks()
    {
        var releasedAtUtc = DateTimeOffset.Parse("2026-08-24T08:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-2095-RELEASE",
            "SKU-2095",
            "PV-2095",
            12m,
            1,
            releasedAtUtc.AddDays(1));
        var operationTasks = new[]
        {
            OperationTask.Queue(
                "org-001", "env-dev", "WO-2095-RELEASE", "OP-10", 10, "WC-MIX", [], releasedAtUtc,
                TimeSpan.FromMinutes(30)),
            OperationTask.Queue(
                "org-001", "env-dev", "WO-2095-RELEASE", "OP-20", 20, "WC-PACK", [], releasedAtUtc,
                TimeSpan.FromMinutes(15)),
        };
        workOrder.ClearDomainEvents();

        workOrder.MarkReleased(operationTasks, WorkOrderReleaseFactTime.NotLaterThan(releasedAtUtc, null));

        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(Assert.Single(workOrder.GetDomainEvents()));
        Assert.Equal(releasedAtUtc, domainEvent.ReleasedAt.Value);
        Assert.Collection(
            domainEvent.OperationTasks,
            first => Assert.Same(operationTasks[0], first),
            second => Assert.Same(operationTasks[1], second));
    }

    [Fact]
    public void WorkOrder_mark_released_rejects_null_operation_tasks_without_changing_state_or_publishing_event()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-2095-NULL",
            "SKU-2095",
            "PV-2095",
            12m,
            1,
            DateTimeOffset.Parse("2026-08-24T08:00:00Z"));
        workOrder.ClearDomainEvents();

        Assert.Throws<ArgumentNullException>(() => workOrder.MarkReleased(null!, WorkOrderReleaseFactTime.NotLaterThan(DateTimeOffset.Parse("2026-08-24T08:00:00Z"), null)));

        Assert.Equal(WorkOrder.CreatedStatus, workOrder.Status);
        Assert.DoesNotContain(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent);
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
    public void ProductionReport_keeps_the_submitting_operator_and_carries_it_onto_the_reversal_row()
    {
        var report = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-OP-001",
            "WO-001",
            "OP-10",
            9m,
            0m,
            false,
            DateTimeOffset.Parse("2026-05-23T09:00:00Z"),
            reportedBy: "  user-emp-010  ");

        Assert.Equal("user-emp-010", report.ReportedBy);

        var reversal = ProductionReport.Reverse(
            report,
            "PRPT-OP-001-R",
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"),
            "reported on the wrong operation",
            "user-supervisor");

        // 冲销行是另一条报工事实，提交它的是执行冲销的人，不是原报工人。
        Assert.Equal("user-supervisor", reversal.ReportedBy);
        Assert.Equal("user-supervisor", reversal.ReversedBy);
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

        _ = workOrder.Release(
            DateTimeOffset.Parse("2026-05-23T08:00:00Z"),
            WorkOrderReleaseFactTime.NotLaterThan(DateTimeOffset.Parse("2026-05-23T08:00:00Z"), null),
            routingSteps);

        Assert.Throws<InvalidOperationException>(() =>
            workOrder.Release(
            DateTimeOffset.Parse("2026-05-23T08:00:00Z"),
            WorkOrderReleaseFactTime.NotLaterThan(DateTimeOffset.Parse("2026-05-23T08:00:00Z"), null),
            routingSteps));
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
    public void OperationTask_rejects_dispatch_until_a_revoked_schedule_is_released()
    {
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-APS-INVALIDATED",
            "OP-10",
            OperationTaskLifecycleStatus.ScheduleInvalidated,
            10,
            "WC-OLD",
            [],
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse("2026-06-01T08:05:00Z"),
            null);

        var exception = Assert.Throws<KnownException>(() => task.Assign(
            "operator-001",
            "DEV-OIL-01",
            "SHIFT-A",
            DateTimeOffset.Parse("2026-06-01T08:10:00Z"),
            "user:operator-001"));

        Assert.Equal("排程已失效的工序任务必须重新排程后才能派工。", exception.Message);
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

        Assert.Contains("生产工单 WO-OVER", exception.Message, StringComparison.Ordinal);
        Assert.Contains("调整报工数量或工单超产容差", exception.Message, StringComparison.Ordinal);
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

        Assert.Contains("生产工单 WO-SCRAP", exception.Message, StringComparison.Ordinal);
        Assert.Contains("调整报工数量或工单超产容差", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkOrder_rejects_progress_above_twenty_percent_even_when_configured_tolerance_is_higher()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-HARD-OVER-LIMIT",
            "SKU-001",
            "PV-001",
            100m,
            10,
            DateTimeOffset.Parse("2026-05-23T10:00:00Z"),
            overReceiptTolerancePercent: 50m);
        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-05-23T08:00:00Z"));

        workOrder.RecordProductionProgress(120m, 0m, DateTimeOffset.Parse("2026-05-23T09:00:00Z"));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            workOrder.RecordProductionProgress(0.000001m, 0m, DateTimeOffset.Parse("2026-05-23T09:01:00Z")));

        Assert.Contains("生产工单 WO-HARD-OVER-LIMIT", exception.Message, StringComparison.Ordinal);
        Assert.Contains("120", exception.Message, StringComparison.Ordinal);
        Assert.Contains("调整报工数量或工单计划量", exception.Message, StringComparison.Ordinal);
        Assert.Equal(120m, workOrder.CompletedQuantity);
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
        Assert.False(request.IsSupplementary);
        Assert.Null(request.OriginalMaterialIssueRequestNo);
        // 创建只发「领料已申请」（仓库据此建出库/拣货，#1324）；库存移动仍然只在收料/退料时发生。
        Assert.Single(request.GetDomainEvents().OfType<MaterialIssueRequestCreatedDomainEvent>());
        Assert.Empty(request.GetDomainEvents().OfType<MaterialIssueRequestedDomainEvent>());
        Assert.Empty(request.GetDomainEvents().OfType<MaterialLineSideReceiptConfirmedDomainEvent>());
    }

    [Fact]
    public void MaterialIssueRequest_creation_tracks_supplementary_semantics_and_original_request_no()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-002",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            3m,
            DateTimeOffset.Parse("2026-05-23T08:10:00Z"),
            isSupplementary: true,
            originalMaterialIssueRequestNo: "MIR-001");

        Assert.True(request.IsSupplementary);
        Assert.Equal("MIR-001", request.OriginalMaterialIssueRequestNo);
    }

    [Fact]
    public void MaterialIssueRequest_creation_rejects_inconsistent_supplementary_semantics()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-23T08:10:00Z");

        Assert.Throws<ArgumentException>(() => MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-003", "WO-001", "OP-10", "MAT-001", "PCS", 3m, timestamp,
            isSupplementary: true));

        Assert.Throws<ArgumentException>(() => MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-004", "WO-001", "OP-10", "MAT-001", "PCS", 3m, timestamp,
            originalMaterialIssueRequestNo: "MIR-001"));
    }

    [Fact]
    public void MaterialIssueRequest_creation_rejects_self_reference()
    {
        Assert.Throws<ArgumentException>(() => MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-005", "WO-001", "OP-10", "MAT-001", "PCS", 3m,
            DateTimeOffset.Parse("2026-05-23T08:10:00Z"),
            isSupplementary: true,
            originalMaterialIssueRequestNo: "MIR-005"));
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

        request.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, 
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
    public void MaterialIssueRequest_waits_for_every_split_warehouse_posting_before_receipt_is_received()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-SPLIT",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            DateTimeOffset.Parse("2026-05-23T08:10:00Z"));
        request.ClearDomainEvents();
        request.ConfirmLineSideReceipt(
            new MaterialTransferLocations(
                "SITE-001",
                "WH-WB-RM-01",
                "SITE-001",
                "WH-WB-LINE-01",
                [
                    new MaterialTransferAllocation("SITE-001", "WH-WB-RM-01", "LOT-A", 3m),
                    new MaterialTransferAllocation("SITE-001", "WH-WB-SF-01", "LOT-B", 2m),
                ]),
            DateTimeOffset.Parse("2026-05-23T08:30:00Z"),
            5m,
            "LOT-WO");

        var token = request.PendingPostingToken!;
        request.MarkInventoryPosted(token, MaterialTransferLeg.WarehouseIssue, DateTimeOffset.Parse("2026-05-23T08:31:00Z"), 0);
        request.MarkInventoryPosted(token, MaterialTransferLeg.LineSideReceipt, DateTimeOffset.Parse("2026-05-23T08:32:00Z"));
        Assert.Equal(0m, request.ReceivedQuantity);

        request.MarkInventoryPosted(token, MaterialTransferLeg.WarehouseIssue, DateTimeOffset.Parse("2026-05-23T08:33:00Z"), 1);

        Assert.Equal(5m, request.ReceivedQuantity);
        Assert.Equal(MaterialIssueRequest.ReceivedStatus, request.Status);
    }

    [Fact]
    public void MaterialIssueRequest_cancel_of_received_material_without_lot_is_a_business_rule_violation()
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
        request.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, DateTimeOffset.Parse("2026-05-23T08:30:00Z"), 5m);
        request.ClearDomainEvents();

        // Received material without a lot cannot be returned to warehouse stock (#557); cancelling
        // must raise a business-rule violation that WorkOrderCancellationOrchestrator maps to a
        // KnownException rather than silently succeeding.
        var exception = Assert.Throws<InvalidOperationException>(
            () => request.CancelForWorkOrderCancellation(DateTimeOffset.Parse("2026-05-23T09:00:00Z")));
        Assert.Contains("received material lot", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void QualityHoldContext_rejects_force_release_before_hold_time()
    {
        var heldAtUtc = DateTimeOffset.Parse("2026-07-13T05:00:00Z");
        var context = QualityHoldContext.Capture(
            "org", "env", "WO-1", null, "business-mes", "WO-1", "QI-1", "PLAN-1",
            "rejected", "quality.InspectionRejected", "defect", heldAtUtc, "quality");

        var exception = Assert.Throws<KnownException>(() =>
            context.ForceRelease("manual override", "user:supervisor", heldAtUtc.AddSeconds(-1)));

        Assert.Equal("质量保留释放时间不能早于保留时间。", exception.Message);
        Assert.True(context.Active);
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

    // 需求源引用是履约追溯链的持久事实。`IReadOnlyList<string>` 只是静态类型上的只读，
    // 直接交出内部 List 时一个向下转型就能在 EF 变更跟踪背后改掉它。
    [Fact]
    public void SourcePlanReference_demand_references_are_not_mutable_through_a_downcast()
    {
        var reference = new SourcePlanReference(
            "demand-planning",
            "planning-suggestion",
            "PS-001",
            "DEMAND-001",
            ["DEMAND-002"]);

        Assert.Equal(new[] { "DEMAND-001", "DEMAND-002" }, reference.SourceDemandReferences);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)reference.SourceDemandReferences!).Add("DEMAND-003"));
        Assert.Equal(2, reference.SourceDemandReferences!.Count);
    }
}
