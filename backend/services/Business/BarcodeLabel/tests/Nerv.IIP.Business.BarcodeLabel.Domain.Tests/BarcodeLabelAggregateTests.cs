using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class BarcodeLabelAggregateTests
{
    [Fact]
    public void Gs1_parser_extracts_gtin_lot_serial_and_quantity()
    {
        var parsed = Gs1ApplicationIdentifierParser.Parse("(01)09506000134352(10)LOT-A(21)SN-0001(30)2");

        Assert.Equal("09506000134352", parsed.Gtin);
        Assert.Equal("LOT-A", parsed.LotNo);
        Assert.Equal("SN-0001", parsed.SerialNumber);
        Assert.Equal(2, parsed.Quantity);
        Assert.Equal("urn:epc:id:sgtin:0950600.013435.SN-0001", parsed.EpcUri);
    }

    [Fact]
    public void Gs1_mod10_generates_expected_gtin_check_digit()
    {
        Assert.Equal("09506000134352", Gs1BarcodeValue.AppendMod10CheckDigit("0950600013435"));
    }

    [Fact]
    public void Gs1_print_batch_persists_serialized_label_fields_and_commissioning_events()
    {
        var rule = BarcodeRule.Create("org-001", "env-dev", "GS1-FG", "gs1-128", "0950600013435", 80, "gs1-mod10", ["wms.inbound"], "active");

        var batch = LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-print-gs1-001",
            """{"skuCode":"SKU-FG-1000","lotNo":"LOT-A","serialPrefix":"SN-"}""",
            2);

        Assert.Equal(["SN-0001", "SN-0002"], batch.Items.Select(x => x.SerialNumber!).ToArray());
        Assert.All(batch.Items, item =>
        {
            Assert.Equal("09506000134352", item.Gtin);
            Assert.Equal("LOT-A", item.LotNo);
            Assert.StartsWith("(01)09506000134352(10)LOT-A(21)SN-", item.LabelValue, StringComparison.Ordinal);
            Assert.StartsWith("urn:epc:id:sgtin:", item.EpcUri, StringComparison.Ordinal);
        });
        Assert.Equal(2, batch.EpcisEvents.Count);
        Assert.All(batch.EpcisEvents, epcisEvent =>
        {
            Assert.Equal("commissioning", epcisEvent.EventType);
            Assert.Equal("ADD", epcisEvent.Action);
            Assert.Equal("commissioning", epcisEvent.BusinessStep);
        });
    }

    [Fact]
    public void Barcode_rule_creation_rejects_blank_prefix()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BarcodeRule.Create("org-001", "env-dev", "FG", "qr", " ", 18, "none", ["wms.inbound"], "active"));

        Assert.Contains("Prefix", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Barcode_rule_creation_rejects_unsupported_type()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BarcodeRule.Create("org-001", "env-dev", "FG", "unsupported", "FG", 18, "none", ["wms.inbound"], "active"));

        Assert.Contains("BarcodeType", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Barcode_label_timestamps_are_offset_utc_values()
    {
        var rule = ActiveRule();
        var template = LabelTemplate.Create(
            "org-001",
            "env-dev",
            "FG_BOX",
            "Finished goods box",
            "tpl-file-001",
            """{"sku":"string"}""",
            "active");
        var batch = NewPrintBatch(rule, "idem-print-001", "ASN-001", 1);
        var scan = NewScan("idem-scan-001", "BC001");

        Assert.Equal(TimeSpan.Zero, rule.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, template.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, batch.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, batch.Items.Single().CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, scan.ScannedAtUtc.Offset);
    }

    [Fact]
    public void Label_template_stores_filestorage_file_id_only()
    {
        var template = LabelTemplate.Create(
            "org-001",
            "env-dev",
            "FG_BOX",
            "Finished goods box",
            "tpl-file-001",
            """{"sku":"string","lot":"string"}""",
            "active");

        Assert.Equal("tpl-file-001", template.TemplateFileId);
        Assert.DoesNotContain("objectKey", template.TemplateFileId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Print_batch_generates_deterministic_items_from_rule_source_and_sequence()
    {
        var rule = ActiveRule();

        var first = LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-print-001",
            """{"sku":"SKU-FG-1000"}""",
            3);
        var second = LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-print-001",
            """{"sku":"SKU-FG-1000"}""",
            3);

        Assert.Equal(first.Items.Select(x => x.LabelValue), second.Items.Select(x => x.LabelValue));
        Assert.Equal(["FGASN0010001", "FGASN0010002", "FGASN0010003"], first.Items.Select(x => x.LabelValue).ToArray());
        Assert.IsType<LabelPrintBatchCreatedDomainEvent>(first.GetDomainEvents().First());
        Assert.Contains(first.GetDomainEvents(), x => x is LabelPrintBatchCompletedDomainEvent);
    }

    [Fact]
    public void Print_batch_idempotency_accepts_same_payload_and_rejects_conflicts()
    {
        var rule = ActiveRule();
        var templateId = new LabelTemplateId(Guid.CreateVersion7());
        var batch = NewPrintBatch(rule, templateId, "idem-print-001", "ASN-001", 2);
        var same = NewPrintBatch(rule, templateId, "idem-print-001", "ASN-001", 2);
        var conflict = NewPrintBatch(rule, templateId, "idem-print-001", "ASN-002", 2);

        Assert.True(batch.HasSameIdempotencyPayload(same));
        Assert.False(batch.HasSameIdempotencyPayload(conflict));
        Assert.Throws<InvalidOperationException>(() => batch.EnsureSameIdempotencyPayload(conflict));
    }

    [Theory]
    [InlineData("", "BC001", "idem-scan-001")]
    [InlineData("PDA-01", "", "idem-scan-001")]
    [InlineData("PDA-01", "BC001", "")]
    public void Scan_record_creation_requires_device_value_and_idempotency(string deviceCode, string scannedValue, string idempotencyKey)
    {
        Assert.Throws<ArgumentException>(() => ScanRecord.Record(
            "org-001",
            "env-dev",
            deviceCode,
            scannedValue,
            "wms.receiving",
            "ASN-001",
            idempotencyKey,
            "accepted",
            null));
    }

    [Fact]
    public void Scan_idempotency_accepts_same_payload_and_rejects_conflicts()
    {
        var scan = NewScan("idem-scan-001", "BC001");
        var same = NewScan("idem-scan-001", "BC001");
        var conflict = NewScan("idem-scan-001", "BC002");

        Assert.True(scan.HasSameIdempotencyPayload(same));
        Assert.False(scan.HasSameIdempotencyPayload(conflict));
        Assert.Throws<InvalidOperationException>(() => scan.EnsureSameIdempotencyPayload(conflict));
        Assert.IsType<LabelScannedDomainEvent>(scan.GetDomainEvents().Single());
    }

    [Fact]
    public void Accepted_gs1_inventory_scan_parses_traceability_fields_and_object_event()
    {
        var scan = ScanRecord.Record(
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

        Assert.Equal("09506000134352", scan.Gtin);
        Assert.Equal("LOT-A", scan.LotNo);
        Assert.Equal("SN-0001", scan.SerialNumber);
        Assert.Equal(2, scan.Quantity);
        Assert.Equal("inventory-movement-requested", scan.BusinessAction);
        Assert.NotNull(scan.DownstreamEventId);
        var epcisEvent = Assert.Single(scan.EpcisEvents);
        Assert.Equal("objectEvent", epcisEvent.EventType);
        Assert.Equal("OBSERVE", epcisEvent.Action);
        Assert.Equal("inventory.receipt", epcisEvent.BusinessStep);
    }

    [Fact]
    public void Accepted_scan_rejects_unsupported_business_workflow()
    {
        var exception = Assert.Throws<ArgumentException>(() => ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A(21)SN-0001",
            "mes.report",
            "WO-001",
            "idem-scan-gs1-002",
            "accepted",
            null,
            "SKU-FG-1000",
            "EA",
            "SITE-01",
            "LINE-01",
            "qualified",
            "owned",
            null,
            1));

        Assert.Contains("SourceWorkflow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejected_scan_raises_scan_rejected_event()
    {
        var scan = ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "BC001",
            "wms.receiving",
            "ASN-001",
            "idem-scan-001",
            "rejected",
            "Rule mismatch");

        Assert.Contains(scan.GetDomainEvents(), x => x is ScanRejectedDomainEvent);
    }

    private static BarcodeRule ActiveRule()
    {
        return BarcodeRule.Create("org-001", "env-dev", "FG", "code128", "FG", 13, "none", ["wms.inbound"], "active");
    }

    private static LabelPrintBatch NewPrintBatch(BarcodeRule rule, string idempotencyKey, string documentId, int quantity)
    {
        return NewPrintBatch(rule, new LabelTemplateId(Guid.CreateVersion7()), idempotencyKey, documentId, quantity);
    }

    private static LabelPrintBatch NewPrintBatch(BarcodeRule rule, LabelTemplateId templateId, string idempotencyKey, string documentId, int quantity)
    {
        return LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            templateId,
            "wms.inbound",
            documentId,
            idempotencyKey,
            """{"sku":"SKU-FG-1000"}""",
            quantity);
    }

    private static ScanRecord NewScan(string idempotencyKey, string scannedValue)
    {
        return ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            scannedValue,
            "wms.receiving",
            "ASN-001",
            idempotencyKey,
            "accepted",
            null);
    }
}
