using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class BarcodeLabelAggregateTests
{
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
