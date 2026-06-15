using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelIntegrationEventTests
{
    [Fact]
    public void Print_batch_created_converter_uses_expected_event_name_and_public_payload()
    {
        var batch = NewPrintBatch();
        var domainEvent = new LabelPrintBatchCreatedDomainEvent(batch);

        var integrationEvent = new LabelPrintBatchCreatedIntegrationEventConverter().Convert(domainEvent);
        var json = JsonSerializer.Serialize(integrationEvent);

        Assert.Equal("barcode.LabelPrintBatchCreated", LabelPrintBatchCreatedIntegrationEvent.EventName);
        Assert.Equal(batch.Id, integrationEvent.PrintBatchId);
        Assert.DoesNotContain("objectKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("object_key", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Print_batch_completed_converter_uses_expected_event_name()
    {
        var batch = NewPrintBatch();
        var integrationEvent = new LabelPrintBatchCompletedIntegrationEventConverter()
            .Convert(new LabelPrintBatchCompletedDomainEvent(batch));

        Assert.Equal("barcode.LabelPrintBatchCompleted", LabelPrintBatchCompletedIntegrationEvent.EventName);
        Assert.Equal(2, integrationEvent.Quantity);
    }

    [Fact]
    public void Scan_converters_use_expected_event_names_and_public_payload()
    {
        var accepted = ScanRecord.Record("org-001", "env-dev", "PDA-01", "BC001", "wms.receiving", "ASN-001", "idem-scan-001", "accepted", null);
        var rejected = ScanRecord.Record("org-001", "env-dev", "PDA-01", "BC002", "wms.receiving", "ASN-001", "idem-scan-002", "rejected", "Unknown barcode");

        var scanned = new LabelScannedIntegrationEventConverter().Convert(new LabelScannedDomainEvent(accepted));
        var scanRejected = new ScanRejectedIntegrationEventConverter().Convert(new ScanRejectedDomainEvent(rejected));
        var json = JsonSerializer.Serialize(new object[] { scanned, scanRejected });

        Assert.Equal("barcode.LabelScanned", LabelScannedIntegrationEvent.EventName);
        Assert.Equal("barcode.ScanRejected", ScanRejectedIntegrationEvent.EventName);
        Assert.Equal("BC001", scanned.ScannedValue);
        Assert.Equal("Unknown barcode", scanRejected.RejectionReason);
        Assert.DoesNotContain("objectKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepted_scan_converter_publishes_shared_barcode_contract_with_parsed_gs1_fields()
    {
        var accepted = NewInventoryScan();

        var integrationEvent = new BarcodeScanAcceptedIntegrationEventConverter()
            .Convert(new LabelScannedDomainEvent(accepted));

        Assert.Equal(BarcodeLabelIntegrationEventTypes.BarcodeScanAccepted, integrationEvent.EventType);
        Assert.Equal(BarcodeLabelIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal("09506000134352", integrationEvent.Payload.Gtin);
        Assert.Equal("LOT-A", integrationEvent.Payload.LotNo);
        Assert.Equal("SN-0001", integrationEvent.Payload.SerialNumber);
        Assert.Equal("inventory.receipt", integrationEvent.Payload.SourceWorkflow);
        Assert.Equal("PDA-01", integrationEvent.Payload.DeviceCode);
    }

    [Fact]
    public void Inventory_scan_converter_routes_accepted_inventory_receipt_to_inventory_movement_request()
    {
        var accepted = NewInventoryScan();

        var integrationEvent = new InventoryMovementRequestedFromBarcodeScanIntegrationEventConverter()
            .Convert(new LabelScannedDomainEvent(accepted));

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal(InventoryIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal("barcode-label", integrationEvent.SourceService);
        Assert.Equal("inbound", integrationEvent.Payload.MovementType);
        Assert.Equal("SKU-FG-1000", integrationEvent.Payload.SkuCode);
        Assert.Equal("LOT-A", integrationEvent.Payload.LotNo);
        Assert.Equal("SN-0001", integrationEvent.Payload.SerialNo);
        Assert.Equal(2, integrationEvent.Payload.Quantity);
    }

    private static LabelPrintBatch NewPrintBatch()
    {
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG", "code128", "FG", 13, "none", ["wms.inbound"], "active");
        return LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-print-001",
            """{"sku":"SKU-FG-1000"}""",
            2);
    }

    private static ScanRecord NewInventoryScan()
    {
        return ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A(21)SN-0001(30)2",
            "inventory.receipt",
            "ASN-001",
            "idem-scan-gs1-001",
            "accepted",
            null,
            "SKU-FG-1000",
            "EA",
            "SITE-01",
            "STAGE-01",
            "qualified",
            "owned",
            null,
            2);
    }
}
