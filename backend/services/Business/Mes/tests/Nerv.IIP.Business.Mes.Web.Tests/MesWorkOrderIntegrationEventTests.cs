using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesWorkOrderIntegrationEventTests
{
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
        Assert.Equal(9m, completed.Payload.GoodQuantity);
        Assert.Equal(1m, completed.Payload.ScrapQuantity);
        Assert.Equal(MesIntegrationEventTypes.WorkOrderClosed, closed.EventType);
        Assert.Equal("WO-001", closed.Payload.WorkOrderId);
    }
}
