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
            [
                new RoutingStepSnapshot("OP-020", 20, "WC-020", [], TimeSpan.FromMinutes(30)),
                new RoutingStepSnapshot("OP-010", 10, "WC-010", [], TimeSpan.FromMinutes(60))
            ]);

        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter()
            .Convert(new WorkOrderReleasedDomainEvent(workOrder, tasks));

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
