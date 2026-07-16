using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesIntegrationEventTests
{
    [Fact]
    public void Manual_dispatch_clear_reason_converter_maps_every_domain_reason_to_wire_contract()
    {
        var expectedCodes = new Dictionary<OperationTaskManualDispatchClearReason, string>
        {
            [OperationTaskManualDispatchClearReason.DeviceCleared] =
                MesManualDispatchClearReasonCodes.DeviceCleared,
            [OperationTaskManualDispatchClearReason.OperationCancelled] =
                MesManualDispatchClearReasonCodes.OperationCancelled
        };
        Assert.Equal(Enum.GetValues<OperationTaskManualDispatchClearReason>(), expectedCodes.Keys);

        var occurredAtUtc = DateTimeOffset.Parse("2026-07-15T08:00:00Z");
        var dispatch = new OperationTaskManualDispatchSnapshot(
            "org-001", "env-dev", "WO-001", "OP-10", 10,
            "DEVICE-2", "WC-1", occurredAtUtc, occurredAtUtc.AddHours(1),
            occurredAtUtc, 2);
        var converter = new OperationTaskManualDispatchClearedIntegrationEventConverter(
            new StubMesIntegrationEventContextAccessor(
                new MesIntegrationEventContext("corr-clear", "cause-clear")));

        foreach (var (reason, expectedCode) in expectedCodes)
        {
            var integrationEvent = converter.Convert(
                new OperationTaskManualDispatchClearedDomainEvent(
                    dispatch, reason, occurredAtUtc, "user:planner-1"));

            Assert.Equal(expectedCode, integrationEvent.Payload.ReasonCode);
        }
    }

    [Fact]
    public void Manual_dispatch_lifecycle_converters_preserve_real_snapshot_revision_actor_and_lineage()
    {
        var start = DateTimeOffset.Parse("2026-07-15T08:00:00Z");
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-1", [],
            start, TimeSpan.FromHours(1));
        task.Assign("operator-1", "DEVICE-2", "SHIFT-1", start.AddMinutes(-5), "user:planner-1");
        var dispatchedDomainEvent = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        var dispatched = new OperationTaskManuallyDispatchedIntegrationEventConverter()
            .Convert(dispatchedDomainEvent);

        task.ClearDomainEvents();
        task.Assign("operator-1", null, "SHIFT-1", start.AddMinutes(-4), "user:planner-1");
        var clearedDomainEvent = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        var cleared = new OperationTaskManualDispatchClearedIntegrationEventConverter(
                new StubMesIntegrationEventContextAccessor(
                    new MesIntegrationEventContext("corr-clear-2", dispatched.EventId)))
            .Convert(clearedDomainEvent);

        Assert.Equal(MesIntegrationEventTypes.OperationTaskManuallyDispatched, dispatched.EventType);
        Assert.Equal(1, dispatched.Payload.DispatchRevision);
        Assert.Equal("DEVICE-2", dispatched.Payload.ResourceId);
        Assert.Equal("user:planner-1", dispatched.Actor);
        Assert.Equal(MesIntegrationEventTypes.OperationTaskManualDispatchCleared, cleared.EventType);
        Assert.Equal(2, cleared.Payload.DispatchRevision);
        Assert.Equal("DEVICE-2", cleared.Payload.ResourceId);
        Assert.Equal(MesManualDispatchClearReasonCodes.DeviceCleared, cleared.Payload.ReasonCode);
        Assert.Equal("corr-clear-2", cleared.CorrelationId);
        Assert.Equal(dispatched.EventId, cleared.CausationId);
        Assert.Equal("user:planner-1", cleared.Actor);
        Assert.NotEqual(dispatched.IdempotencyKey, cleared.IdempotencyKey);
    }

    [Fact]
    public void Production_report_converter_emits_inventory_outbound_requests_from_production_line_side_account()
    {
        var reportedAtUtc = DateTimeOffset.Parse("2026-06-15T08:00:00Z");
        var consumption = ProductionReportMaterialConsumption.Record(
            "org-001",
            "env-dev",
            "PRPT-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "LOT-OIL-A",
            "L",
            2.5m,
            "MIR-001");

        var integrationEvent = new ProductionMaterialConsumedIntegrationEventConverter()
            .Convert(new ProductionMaterialConsumedDomainEvent(consumption));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("business-mes", integrationEvent.SourceService);
        Assert.Equal("outbound", integrationEvent.Payload.MovementType);
        Assert.Equal("business-mes", integrationEvent.Payload.SourceService);
        Assert.Equal("PRPT-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.SkuCode);
        Assert.Equal("production", integrationEvent.Payload.SiteCode);
        Assert.Equal("line-side", integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-OIL-A", integrationEvent.Payload.LotNo);
        Assert.Equal(-2.5m, integrationEvent.Payload.Quantity);
        Assert.Equal("PRPT-001", integrationEvent.CorrelationId);
        Assert.Contains("MIR-001", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Finished_goods_receipt_converter_emits_inventory_inbound_request()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-001",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001",
            null,
            12.34m);

        var domainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());
        var integrationEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter()
            .Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("FGR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("SKU-FG", integrationEvent.Payload.SkuCode);
        Assert.Equal("LOT-FG-001", integrationEvent.Payload.LotNo);
        Assert.Equal(8m, integrationEvent.Payload.Quantity);
        Assert.Equal(12.34m, integrationEvent.Payload.UnitCost);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
    }

    [Fact]
    public void Finished_goods_receipt_waits_for_erp_cost_before_emitting_inventory_request()
    {
        var request = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-LEGACY",
            "WO-001",
            "SKU-FG",
            8m,
            "PCS",
            DateTimeOffset.Parse("2026-06-15T09:00:00Z"),
            "LOT-FG-001");

        Assert.Empty(request.GetDomainEvents());

        request.ApplyCapitalizedUnitCost(12.34m);

        var domainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(request.GetDomainEvents().Single());
        var integrationEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter()
            .Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("FGR-LEGACY", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal(12.34m, integrationEvent.Payload.UnitCost);
    }

    [Fact]
    public void Material_issue_converter_emits_inventory_outbound_request_for_confirmed_pick()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            3m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmLineSideReceipt(
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            3m,
            "LOT-OIL-A");

        var integrationEvent = new MaterialIssueRequestedIntegrationEventConverter()
            .Convert(new MaterialIssueRequestedDomainEvent(request, 3m));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("outbound", integrationEvent.Payload.MovementType);
        Assert.Equal("MIR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.SkuCode);
        Assert.Equal("L", integrationEvent.Payload.UomCode);
        Assert.Equal("warehouse", integrationEvent.Payload.SiteCode);
        Assert.Equal("line-side", integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-OIL-A", integrationEvent.Payload.LotNo);
        Assert.Equal(-3m, integrationEvent.Payload.Quantity);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
    }

    [Fact]
    public void Line_side_receipt_converter_emits_inventory_inbound_request_to_production_line_side_account()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            3m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmLineSideReceipt(
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            3m,
            "LOT-OIL-A");

        var integrationEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter()
            .Convert(new MaterialLineSideReceiptConfirmedDomainEvent(request, 3m));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("MIR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("OP-10", integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal("MAT-OIL", integrationEvent.Payload.SkuCode);
        Assert.Equal("L", integrationEvent.Payload.UomCode);
        Assert.Equal("production", integrationEvent.Payload.SiteCode);
        Assert.Equal("line-side", integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-OIL-A", integrationEvent.Payload.LotNo);
        Assert.Equal(3m, integrationEvent.Payload.Quantity);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
        Assert.Contains("line-side-receipt", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_side_return_converters_emit_inventory_reversal_requests()
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-OIL",
            "L",
            3m,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"));
        request.ConfirmLineSideReceipt(
            DateTimeOffset.Parse("2026-06-15T08:15:00Z"),
            3m,
            "LOT-OIL-A");
        request.ReturnLineSideMaterial(DateTimeOffset.Parse("2026-06-15T10:00:00Z"), 1m);

        var productionOutbound = new MaterialLineSideReturnRequestedIntegrationEventConverter()
            .Convert(new MaterialLineSideReturnRequestedDomainEvent(request, 1m, "LOT-MAT-001", DateTimeOffset.Parse("2026-06-01T08:30:00Z")));
        var warehouseInbound = new MaterialReturnedToWarehouseIntegrationEventConverter()
            .Convert(new MaterialReturnedToWarehouseDomainEvent(request, 1m, "LOT-MAT-001", DateTimeOffset.Parse("2026-06-01T08:30:00Z")));

        Assert.Equal("outbound", productionOutbound.Payload.MovementType);
        Assert.Equal("production", productionOutbound.Payload.SiteCode);
        Assert.Equal("line-side", productionOutbound.Payload.LocationCode);
        Assert.Equal(-1m, productionOutbound.Payload.Quantity);
        Assert.Equal("inbound", warehouseInbound.Payload.MovementType);
        Assert.Equal("warehouse", warehouseInbound.Payload.SiteCode);
        Assert.Equal("line-side", warehouseInbound.Payload.LocationCode);
        Assert.Equal(1m, warehouseInbound.Payload.Quantity);
    }

    [Fact]
    public void Defect_converter_emits_quality_defect_raised_event()
    {
        var defect = DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-001",
            "WO-001",
            "OP-10",
            "SURFACE",
            1m,
            DateTimeOffset.Parse("2026-06-15T10:00:00Z"));

        var integrationEvent = new DefectRaisedIntegrationEventConverter()
            .Convert(new DefectRaisedDomainEvent(defect));

        Assert.Equal(QualityIntegrationEventTypes.DefectRaised, integrationEvent.EventType);
        Assert.Equal("business-mes", integrationEvent.SourceService);
        Assert.Equal("DEF-001", integrationEvent.Payload.DefectNo);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("SURFACE", integrationEvent.Payload.DefectCode);
        Assert.Equal(1m, integrationEvent.Payload.Quantity);
    }

    [Fact]
    public void Production_report_converter_emits_oee_projection_fact_with_standard_rate_snapshot()
    {
        var reportedAtUtc = DateTimeOffset.Parse("2026-07-10T08:45:00Z");
        var report = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-OEE-001",
            "WO-001",
            "OP-10",
            80m,
            10m,
            false,
            reportedAtUtc,
            10m,
            oeeProjection: new ProductionReportOeeProjection("WC-PACK-01", "DEV-PACK-01", "PCS", 100m));

        var domainEvent = Assert.IsType<ProductionReportRecordedDomainEvent>(report.GetDomainEvents().Single());
        var integrationEvent = new ProductionReportRecordedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal(MesIntegrationEventTypes.ProductionReportRecorded, integrationEvent.EventType);
        Assert.Equal("DEV-PACK-01", integrationEvent.Payload.DeviceAssetId);
        Assert.Equal("WC-PACK-01", integrationEvent.Payload.WorkCenterId);
        Assert.Equal(100m, integrationEvent.Payload.TheoreticalRatePerHour);
        Assert.Equal(80m, integrationEvent.Payload.GoodQuantity);
        Assert.Equal(10m, integrationEvent.Payload.ScrapQuantity);
        Assert.Equal(10m, integrationEvent.Payload.ReworkQuantity);
    }

    [Fact]
    public void Production_report_reversal_converter_reuses_the_original_oee_snapshot()
    {
        var original = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PRPT-OEE-ORIGINAL-001",
            "WO-001",
            "OP-10",
            80m,
            10m,
            false,
            DateTimeOffset.Parse("2026-07-10T08:45:00Z"),
            10m,
            oeeProjection: new ProductionReportOeeProjection("WC-PACK-01", "DEV-PACK-01", "PCS", 100m));
        var reversal = ProductionReport.Reverse(
            original,
            "PRPT-OEE-REVERSAL-001",
            DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
            "operator correction",
            "operator-1");

        var domainEvent = Assert.IsType<ProductionReportRecordedDomainEvent>(reversal.GetDomainEvents().Single());
        var integrationEvent = new ProductionReportRecordedIntegrationEventConverter().Convert(domainEvent);

        Assert.True(integrationEvent.Payload.IsReversal);
        Assert.Equal(original.ReportNo, integrationEvent.Payload.ReversedReportNo);
        Assert.Equal("WC-PACK-01", integrationEvent.Payload.WorkCenterId);
        Assert.Equal("DEV-PACK-01", integrationEvent.Payload.DeviceAssetId);
        Assert.Equal("PCS", integrationEvent.Payload.UomCode);
        Assert.Equal(100m, integrationEvent.Payload.TheoreticalRatePerHour);
    }

    private sealed class StubMesIntegrationEventContextAccessor(MesIntegrationEventContext context)
        : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => context;
    }
}
