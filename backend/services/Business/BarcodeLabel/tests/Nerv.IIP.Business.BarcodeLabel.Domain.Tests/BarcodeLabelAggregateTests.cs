using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
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
        Assert.Equal(string.Empty, parsed.EpcUri);
    }

    [Fact]
    public void Gs1_parser_extracts_raw_gs1_128_with_fnc1_separators()
    {
        var parsed = Gs1ApplicationIdentifierParser.Parse("010950600013435210LOT-A\u001D21SN-0001\u001D302");

        Assert.Equal("09506000134352", parsed.Gtin);
        Assert.Equal("LOT-A", parsed.LotNo);
        Assert.Equal("SN-0001", parsed.SerialNumber);
        Assert.Equal(2, parsed.Quantity);
    }

    [Fact]
    public void Gs1_parser_skips_known_non_core_ais_and_keeps_serialized_identity()
    {
        var parsed = Gs1ApplicationIdentifierParser.Parse("0109506000134352112401011726123110LOT-A\u001D21SN-0001\u001D3103001234");

        Assert.Equal("09506000134352", parsed.Gtin);
        Assert.Equal("LOT-A", parsed.LotNo);
        Assert.Equal("SN-0001", parsed.SerialNumber);
    }

    [Fact]
    public void Gs1_parser_extracts_sscc_from_raw_and_parenthesized_values()
    {
        var raw = Gs1ApplicationIdentifierParser.Parse("00123456789012345675");
        var parenthesized = Gs1ApplicationIdentifierParser.Parse("(00)123456789012345675");

        Assert.Equal("123456789012345675", raw.Sscc);
        Assert.Equal(raw.Sscc, parenthesized.Sscc);
    }

    [Fact]
    public void Gs1_generation_inserts_fnc1_after_variable_length_ais()
    {
        var value = new Gs1BarcodeValue("09506000134352", "LOT-A", "SN-0001", 2);

        var label = value.ToAiString();

        Assert.Equal("(01)09506000134352(10)LOT-A\u001D(21)SN-0001\u001D(30)2", label);
        var parsed = Gs1ApplicationIdentifierParser.Parse(label);
        Assert.Equal("LOT-A", parsed.LotNo);
        Assert.Equal("SN-0001", parsed.SerialNumber);
        Assert.Equal(2, parsed.Quantity);
    }

    [Fact]
    public void Gs1_sscc_generation_appends_mod10_check_digit()
    {
        var value = Gs1BarcodeValue.CreateSscc("12345678901234567");

        Assert.Equal("123456789012345675", value.Sscc);
        Assert.Equal("(00)123456789012345675", value.ToAiString());
    }

    [Fact]
    public void Gs1_mod10_generates_expected_gtin_check_digit()
    {
        Assert.Equal("09506000134352", Gs1BarcodeValue.AppendMod10CheckDigit("0950600013435"));
    }

    [Theory]
    [InlineData(6, "urn:epc:id:sgtin:095060.0013435.SN-0001")]
    [InlineData(7, "urn:epc:id:sgtin:0950600.013435.SN-0001")]
    [InlineData(12, "urn:epc:id:sgtin:095060001343.5.SN-0001")]
    public void Gs1_epc_uri_uses_explicit_company_prefix_length(int companyPrefixLength, string expected)
    {
        var value = Gs1BarcodeValue.Create("0950600013435", "LOT-A", "SN-0001", companyPrefixLength);

        Assert.Equal(expected, value.EpcUri);
    }

    [Theory]
    [InlineData("095060001343")]
    [InlineData("09506000134352")]
    public void Gs1_mod10_rejects_non_13_digit_gtin_root(string digitsWithoutCheckDigit)
    {
        var exception = Assert.Throws<ArgumentException>(() => Gs1BarcodeValue.AppendMod10CheckDigit(digitsWithoutCheckDigit));

        Assert.Contains("13", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gs1_print_batch_persists_serialized_label_fields_and_commissioning_events()
    {
        var rule = BarcodeRule.Create("org-001", "env-dev", "GS1-FG", "gs1-128", "0950600013435", 80, "gs1-mod10", ["wms.inbound"], "active", 7);

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
            Assert.StartsWith("(01)09506000134352(10)LOT-A\u001D(21)SN-", item.LabelValue, StringComparison.Ordinal);
            Assert.StartsWith("urn:epc:id:sgtin:0950600.013435.", item.EpcUri, StringComparison.Ordinal);
        });
        Assert.Equal(2, batch.EpcisEvents.Count);
        Assert.All(batch.EpcisEvents, epcisEvent =>
        {
            Assert.Equal("commissioning", epcisEvent.EventType);
            Assert.Equal("ADD", epcisEvent.Action);
            Assert.Equal("urn:epcglobal:cbv:bizstep:commissioning", epcisEvent.BusinessStep);
        });
    }

    [Theory]
    [InlineData("""{"skuCode":"SKU-FG-1000","serialPrefix":"SN-"}""")]
    [InlineData("""{"skuCode":"SKU-FG-1000","lotNo":"LOT-A"}""")]
    public void Gs1_print_batch_rejects_missing_lot_or_serial_prefix(string labelValuesJson)
    {
        var rule = BarcodeRule.Create("org-001", "env-dev", "GS1-FG", "gs1-128", "0950600013435", 80, "gs1-mod10", ["wms.inbound"], "active", 7);

        Assert.Throws<ArgumentException>(() => LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-print-gs1-001",
            labelValuesJson,
            1));
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
        Assert.DoesNotContain(first.GetDomainEvents(), x => x is LabelPrintBatchCompletedDomainEvent);
    }

    [Fact]
    public void Print_batch_moves_from_pending_to_sent_then_printed_only_after_a_printer_result()
    {
        var batch = NewPrintBatch(ActiveRule(), "idem-print-lifecycle-001", "ASN-001", 1);

        Assert.Equal("pending", batch.Status);
        Assert.Equal("created", batch.Items.Single().Status);
        Assert.Null(batch.CompletedAtUtc);

        batch.RecordSentToPrinter("printer-zpl-01", "job-001");
        batch.RecordPrinted();

        Assert.Equal("printed", batch.Status);
        Assert.Equal("printer-zpl-01", batch.PrinterId);
        Assert.Equal("job-001", batch.PrintJobId);
        Assert.NotNull(batch.CompletedAtUtc);
        Assert.Equal("printed", batch.Items.Single().Status);
    }

    [Fact]
    public void Printed_item_can_be_reprinted_or_voided_but_voided_item_cannot_be_reprinted()
    {
        var batch = NewPrintBatch(ActiveRule(), "idem-print-lifecycle-002", "ASN-001", 1);
        batch.RecordSentToPrinter("printer-zpl-01", "job-002");
        batch.RecordPrinted();

        batch.ReprintItem(1);
        Assert.Equal("reprinted", batch.Items.Single().Status);

        batch.VoidItem(1, "damaged");
        Assert.Equal("voided", batch.Items.Single().Status);
        Assert.Equal("damaged", batch.Items.Single().VoidReason);
        Assert.Throws<InvalidOperationException>(() => batch.ReprintItem(1));
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
        Assert.Contains(scan.GetDomainEvents(), x => x is InventoryMovementRequestedFromScanDomainEvent);
        var epcisEvent = Assert.Single(scan.EpcisEvents);
        Assert.Equal("objectEvent", epcisEvent.EventType);
        Assert.Equal("OBSERVE", epcisEvent.Action);
        Assert.Equal("urn:epcglobal:cbv:bizstep:receiving", epcisEvent.BusinessStep);
    }

    [Fact]
    public void Accepted_scan_with_sscc_and_sgtin_creates_epcis_aggregation_event()
    {
        var scan = ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(00)123456789012345675(01)09506000134352(10)LOT-A\u001D(21)SN-0001",
            "wms.receiving",
            "ASN-001",
            "idem-scan-aggregation-001",
            "accepted",
            null);

        Assert.Equal("123456789012345675", scan.Sscc);
        var aggregation = Assert.Single(scan.EpcisEvents, x => x.EventType == "aggregationEvent");
        Assert.Equal("ADD", aggregation.Action);
        Assert.Equal("urn:epcglobal:cbv:bizstep:packing", aggregation.BusinessStep);
        Assert.Equal("123456789012345675", aggregation.ParentSscc);
        Assert.Equal(scan.Gtin, aggregation.Gtin);
        Assert.Equal(scan.SerialNumber, aggregation.SerialNumber);
    }

    [Fact]
    public void Epcis_disaggregation_event_preserves_parent_child_semantics()
    {
        var scan = ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(00)123456789012345675(01)09506000134352(10)LOT-A\u001D(21)SN-0001",
            "wms.receiving",
            "ASN-001",
            "idem-scan-disaggregation-001",
            "accepted",
            null);

        var disaggregation = EpcisEvent.Disaggregation("org-001", "env-dev", scan, "wms.receiving", "ASN-001");

        Assert.Equal("aggregationEvent", disaggregation.EventType);
        Assert.Equal("DELETE", disaggregation.Action);
        Assert.Equal("urn:epcglobal:cbv:bizstep:unpacking", disaggregation.BusinessStep);
        Assert.Equal("123456789012345675", disaggregation.ParentSscc);
        Assert.Equal(scan.Gtin, disaggregation.Gtin);
        Assert.Equal(scan.SerialNumber, disaggregation.SerialNumber);
    }

    [Fact]
    public void Accepted_issue_scan_with_sscc_does_not_create_packing_aggregation_event()
    {
        var scan = ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(00)123456789012345675(01)09506000134352(21)SN-0001",
            "inventory.issue",
            "OUT-001",
            "idem-scan-issue-aggregation-001",
            "accepted",
            null,
            "SKU-FG-1000",
            "EA",
            "SITE-01",
            "STAGE-01",
            "qualified",
            "owned",
            null,
            1);

        Assert.DoesNotContain(scan.EpcisEvents, x => x.EventType == "aggregationEvent");
        Assert.Single(scan.EpcisEvents, x => x.EventType == "objectEvent");
    }

    [Fact]
    public void Accepted_inventory_scan_does_not_use_gs1_ai30_as_movement_quantity()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A(21)SN-0001(30)24",
            "inventory.receipt",
            "ASN-001",
            "idem-scan-gs1-ai30",
            "accepted",
            null,
            "SKU-FG-1000",
            "EA",
            "SITE-01",
            "STAGE-01",
            "qualified",
            "owned",
            null,
            null));

        Assert.Contains("Quantity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepted_non_inventory_scan_does_not_raise_inventory_movement_domain_event()
    {
        var scan = ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "BC001",
            "wms.receiving",
            "ASN-001",
            "idem-scan-wms-001",
            "accepted",
            null);

        Assert.Contains(scan.GetDomainEvents(), x => x is LabelScannedDomainEvent);
        Assert.DoesNotContain(scan.GetDomainEvents(), x => x is InventoryMovementRequestedFromScanDomainEvent);
        Assert.Equal("wms-receiving-scan-observed", scan.BusinessAction);
        Assert.NotNull(scan.DownstreamEventId);
    }

    [Theory]
    [InlineData("production.report", "production-report-scan-observed")]
    [InlineData("quality.inspection", "quality-inspection-scan-observed")]
    [InlineData("inventory.count", "inventory-count-scan-observed")]
    public void Accepted_non_movement_workflows_select_downstream_scan_action(string sourceWorkflow, string expectedAction)
    {
        var scan = ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A\u001D(21)SN-0001",
            sourceWorkflow,
            "DOC-001",
            $"idem-{sourceWorkflow}",
            "accepted",
            null);

        Assert.Equal(expectedAction, scan.BusinessAction);
        Assert.NotNull(scan.DownstreamEventId);
        Assert.DoesNotContain(scan.GetDomainEvents(), x => x is InventoryMovementRequestedFromScanDomainEvent);
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
