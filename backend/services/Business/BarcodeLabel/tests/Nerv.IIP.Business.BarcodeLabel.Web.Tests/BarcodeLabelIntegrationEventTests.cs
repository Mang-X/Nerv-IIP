using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEvents;

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
}
