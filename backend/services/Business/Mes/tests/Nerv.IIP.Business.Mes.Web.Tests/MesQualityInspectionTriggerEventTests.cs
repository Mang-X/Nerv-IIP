using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesQualityInspectionTriggerEventTests
{
    [Fact]
    public void Operation_task_completed_event_preserves_routing_quality_inspection_flag()
    {
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-MIX",
            [],
            DateTimeOffset.Parse("2026-07-05T07:00:00Z"),
            TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse("2026-07-05T07:30:00Z"),
            null,
            requiresQualityInspection: true);
        task.Complete(DateTimeOffset.Parse("2026-07-05T08:00:00Z"));

        var integrationEvent = new OperationTaskCompletedIntegrationEventConverter()
            .Convert(new OperationTaskCompletedDomainEvent(task));

        Assert.Equal(MesIntegrationEventTypes.OperationTaskCompleted, integrationEvent.EventType);
        Assert.True(integrationEvent.Payload.RequiresQualityInspection);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("OP-10", integrationEvent.Payload.OperationTaskId);
        Assert.Equal("WC-MIX", integrationEvent.Payload.WorkCenterId);
    }

    [Fact]
    public void Finished_goods_receipt_requested_event_is_available_for_quality_final_inspection()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG-1000",
            5m,
            "pcs",
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"),
            "LOT-FG-001",
            null,
            12.5m);
        var domainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());

        var integrationEvent = new FinishedGoodsReceiptRequestedForQualityIntegrationEventConverter()
            .Convert(domainEvent);

        Assert.Equal(MesIntegrationEventTypes.FinishedGoodsReceiptRequested, integrationEvent.EventType);
        Assert.Equal("FGR-001", integrationEvent.Payload.RequestNo);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("SKU-FG-1000", integrationEvent.Payload.SkuCode);
        Assert.Equal("LOT-FG-001", integrationEvent.Payload.ProducedLotNo);
    }
}
