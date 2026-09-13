using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesWorkOrderIntegrationEventTests
{
    [Fact]
    public void Rework_work_order_created_converter_emits_versioned_source_receipt()
    {
        var requestedAtUtc = new DateTimeOffset(2026, 8, 29, 8, 0, 0, TimeSpan.Zero);
        var workOrder = WorkOrder.CreateRework(
            "org-001",
            "env-dev",
            "WO-RW-001",
            "SKU-001",
            "PV-001",
            "PCS",
            3m,
            100,
            requestedAtUtc.AddDays(1),
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

        var integrationEvent = new ReworkWorkOrderCreatedIntegrationEventConverter()
            .Convert(Assert.IsType<ReworkWorkOrderCreatedDomainEvent>(Assert.Single(workOrder.GetDomainEvents())));

        Assert.Equal(MesIntegrationEventTypes.ReworkWorkOrderCreated, integrationEvent.EventType);
        Assert.Equal(MesIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("corr-001", integrationEvent.CorrelationId);
        Assert.Equal("evt-rework-requested-001", integrationEvent.CausationId);
        Assert.Equal("WO-RW-001", integrationEvent.Payload.ReworkWorkOrderId);
        Assert.Equal("WO-SOURCE-001", integrationEvent.Payload.SourceWorkOrderId);
        Assert.Equal("OP-SOURCE-10", integrationEvent.Payload.SourceOperationTaskId);
        Assert.Equal("ncr-001", integrationEvent.Payload.SourceNcrId);
        Assert.Equal("NCR-2026-0001", integrationEvent.Payload.SourceNcrCode);
        Assert.Equal("SKU-001", integrationEvent.Payload.SkuCode);
        Assert.Equal(3m, integrationEvent.Payload.Quantity);
        Assert.Equal("LOT-001", integrationEvent.Payload.SourceLotNo);
        Assert.Equal("SN-001", integrationEvent.Payload.SourceSerialNo);
    }

    [Fact]
    public void Work_order_released_converter_emits_public_mes_event_for_scheduling()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-001",
            "PV-001",
            10,
            1,
            new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero),
            "EA");
        var tasks = workOrder.Release(
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            WorkOrderReleaseFactTime.NotLaterThan(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), null),
            [
                new RoutingStepSnapshot("OP-020", 20, "WC-020", [], TimeSpan.FromMinutes(30)),
                new RoutingStepSnapshot("OP-010", 10, "WC-010", [], TimeSpan.FromMinutes(60))
            ]);

        // 发布时刻远早于「现在」：转换器一旦回落 UtcNow，下面两条断言都红。
        var releasedAtUtc = new DateTimeOffset(2026, 5, 20, 6, 30, 0, TimeSpan.Zero);
        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter()
            .Convert(new WorkOrderReleasedDomainEvent(
                workOrder,
                tasks,
                WorkOrderReleaseFactTime.NotLaterThan(releasedAtUtc, null),
                new Dictionary<string, decimal>()));

        Assert.Equal(MesIntegrationEventTypes.WorkOrderReleased, integrationEvent.EventType);
        Assert.Equal(MesIntegrationEventSources.BusinessMes, integrationEvent.SourceService);
        Assert.Equal(integrationEvent.IdempotencyKey, integrationEvent.CorrelationId);
        Assert.Equal("WO-001", integrationEvent.CausationId);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("SKU-001", integrationEvent.Payload.SkuCode);
        Assert.Equal(10, integrationEvent.Payload.PlannedQuantity);
        Assert.Equal(["OP-010", "OP-020"], integrationEvent.Payload.Operations.Select(x => x.OperationId));
        Assert.Equal(releasedAtUtc, integrationEvent.Payload.ReleasedAtUtc);
        Assert.Equal(releasedAtUtc, integrationEvent.OccurredAtUtc);
    }

    /// <summary>
    /// #3129：转换器把域事件里按工序的「下达前既有净良品量」原样落进载荷；
    /// 字典里没有的工序落 <c>0</c>，**不是** <c>null</c>——null 在契约上专留给
    /// 本次发布之前入队的旧消息（见 <c>ReleasedOperationPayload.PreReleaseGoodQuantity</c>）。
    /// </summary>
    [Fact]
    public void Work_order_released_converter_carries_per_operation_pre_release_good_quantity()
    {
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 10, 1,
            new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero), "EA");
        var tasks = workOrder.Release(
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            WorkOrderReleaseFactTime.NotLaterThan(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), null),
            [
                new RoutingStepSnapshot("OP-010", 10, "WC-010", [], TimeSpan.FromMinutes(60)),
                new RoutingStepSnapshot("OP-020", 20, "WC-020", [], TimeSpan.FromMinutes(30))
            ]);

        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter()
            .Convert(new WorkOrderReleasedDomainEvent(
                workOrder,
                tasks,
                WorkOrderReleaseFactTime.NotLaterThan(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), null),
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["OP-020"] = 250m }));

        Assert.Equal(
            [("OP-010", 0m), ("OP-020", 250m)],
            integrationEvent.Payload.Operations.Select(x => (x.OperationId, x.PreReleaseGoodQuantity)));
    }

    /// <summary>
    /// #3129：<c>Release</c> 当场建出全新工序任务，那些 OperationTaskId 在此刻之前不存在、
    /// 不可能已有报工，故每道工序恒为 <c>0</c>。这条用例钉的是「恒 0」这个结论本身，
    /// 它是该重载**自身**的性质（工序由方法体创建），不是调用方的性质。
    /// </summary>
    [Fact]
    public void Release_of_a_freshly_routed_work_order_carries_zero_pre_release_quantity_for_every_operation()
    {
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 10, 1,
            new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero), "EA");
        var tasks = workOrder.Release(
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            WorkOrderReleaseFactTime.NotLaterThan(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), null),
            [
                new RoutingStepSnapshot("OP-010", 10, "WC-010", [], TimeSpan.FromMinutes(60)),
                new RoutingStepSnapshot("OP-020", 20, "WC-020", [], TimeSpan.FromMinutes(30))
            ]);

        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        Assert.Empty(domainEvent.PreReleaseGoodQuantityByOperationTaskId);
        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter().Convert(domainEvent);
        Assert.Equal(
            [0m, 0m],
            integrationEvent.Payload.Operations.Select(x => x.PreReleaseGoodQuantity));
        Assert.Equal(tasks.Count, integrationEvent.Payload.Operations.Count);
    }

    [Fact]
    public void Work_order_completed_and_closed_converters_emit_public_mes_events()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-001",
            "PV-001",
            10,
            1,
            new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero),
            "EA");
        workOrder.MarkReleased();
        workOrder.Start(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        workOrder.RecordProductionProgress(9m, 1m, new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        workOrder.Close(new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero));

        var completed = new WorkOrderCompletedIntegrationEventConverter()
            .Convert(new WorkOrderCompletedDomainEvent(workOrder, new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)));
        var closed = new WorkOrderClosedIntegrationEventConverter()
            .Convert(new WorkOrderClosedDomainEvent(workOrder, new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero)));

        Assert.Equal(MesIntegrationEventTypes.WorkOrderCompleted, completed.EventType);
        Assert.Equal(completed.IdempotencyKey, completed.CorrelationId);
        Assert.Equal("WO-001", completed.CausationId);
        Assert.Equal(9m, completed.Payload.GoodQuantity);
        Assert.Equal(1m, completed.Payload.ScrapQuantity);
        Assert.Equal(MesIntegrationEventTypes.WorkOrderClosed, closed.EventType);
        Assert.Equal(closed.IdempotencyKey, closed.CorrelationId);
        Assert.Equal("WO-001", closed.CausationId);
        Assert.Equal("WO-001", closed.Payload.WorkOrderId);
    }

    [Fact]
    public void Work_order_cancelled_converter_emits_inventory_reservation_release_request()
    {
        var cancelledAtUtc = new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero);
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-695",
            "SKU-001",
            "PV-001",
            10,
            1,
            cancelledAtUtc.AddHours(4),
            "EA");
        workOrder.MarkReleased();
        workOrder.Cancel("plan cancelled", cancelledAtUtc, ["MIR-001"]);

        var integrationEvent = new WorkOrderCancelledIntegrationEventConverter()
            .Convert(Assert.IsType<WorkOrderCancelledDomainEvent>(workOrder.GetDomainEvents().Last()));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryReservationReleaseRequested, integrationEvent.EventType);
        Assert.Equal(InventoryIntegrationEventSources.BusinessMes, integrationEvent.SourceService);
        Assert.Equal("WO-695", integrationEvent.CausationId);
        Assert.Equal("WO-695", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal(["MIR-001"], integrationEvent.Payload.SourceDocumentLineIds);
    }
}
