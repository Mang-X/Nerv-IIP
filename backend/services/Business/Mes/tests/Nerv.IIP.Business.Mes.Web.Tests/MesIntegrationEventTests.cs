using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesIntegrationEventTests
{
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
            null);

        var integrationEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter()
            .Convert(new FinishedGoodsReceiptRequestedDomainEvent(request));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("FGR-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("SKU-FG", integrationEvent.Payload.SkuCode);
        Assert.Equal("LOT-FG-001", integrationEvent.Payload.LotNo);
        Assert.Equal(8m, integrationEvent.Payload.Quantity);
        Assert.Equal("WO-001", integrationEvent.CorrelationId);
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
}
