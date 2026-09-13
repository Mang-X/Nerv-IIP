using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class LabelPrintBatchSerialAllocationTests
{
    [Fact]
    public void Serial_formatter_uses_the_requested_width_and_monotonic_value()
    {
        var first = LabelSerialNumber.Format(1, 11);
        var next = LabelSerialNumber.Format(2, 11);

        Assert.Equal(11, first.Length);
        Assert.True(StringComparer.Ordinal.Compare(first, next) < 0);
    }

    [Fact]
    public void Plain_rule_allocates_within_the_space_left_after_its_prefix()
    {
        var rule = BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG-13",
            "code128",
            "FG",
            13,
            "none",
            ["wms.inbound"],
            "active");

        Assert.Equal(11, rule.AllocatedSerialNumberLength);
    }

    [Fact]
    public void Gs1_rule_uses_the_full_ai_21_serial_capacity()
    {
        Assert.Equal(20, Gs1Rule().AllocatedSerialNumberLength);
    }

    [Theory]
    [InlineData("code128")]
    [InlineData("qr")]
    [InlineData("datamatrix")]
    public void Allocated_plain_batch_persists_each_rule_serial_in_sequence_order(string barcodeType)
    {
        var rule = PlainRule(barcodeType);

        var batch = Create(rule, "{}", ["00000000001", "00000000002"]);

        Assert.Equal([1, 2], batch.Items.Select(item => item.SequenceNo).ToArray());
        Assert.Equal(["00000000001", "00000000002"], batch.Items.Select(item => item.SerialNumber!).ToArray());
        Assert.Equal(["FG00000000001", "FG00000000002"], batch.Items.Select(item => item.LabelValue).ToArray());
    }

    [Fact]
    public void Newly_allocated_batch_is_reserved_and_cannot_be_dispatched()
    {
        var batch = Create(PlainRule("code128"), "{}", ["00000000001"]);

        var exception = Assert.Throws<LabelPrintLifecycleRejectedException>(batch.EnsureCanBeDispatched);

        Assert.Equal("reserved", batch.Status);
        Assert.Equal(LabelPrintLifecycleRejectionReason.BatchCannotBeDispatched, exception.Reason);
    }

    [Fact]
    public void Mes_association_activates_the_reserved_batch_for_dispatch()
    {
        var batch = Create(PlainRule("code128"), "{}", ["00000000001"]);

        batch.Activate("report-id-001", "PR-001");
        batch.EnsureCanBeDispatched();
        batch.RecordSentToPrinter("printer-01", "job-001");

        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
    }

    [Fact]
    public void Replaying_the_same_mes_association_returns_the_existing_ready_batch()
    {
        var batch = Create(PlainRule("code128"), "{}", ["00000000001"]);
        batch.Activate("report-id-001", "PR-001");

        batch.Activate("report-id-001", "PR-001");

        Assert.Equal("ready-to-print", batch.Status);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
    }

    [Fact]
    public void Replaying_the_same_mes_association_after_dispatch_preserves_the_sent_batch()
    {
        var batch = Create(PlainRule("code128"), "{}", ["00000000001"]);
        batch.Activate("report-id-001", "PR-001");
        batch.RecordSentToPrinter("printer-01", "job-001");

        batch.Activate("report-id-001", "PR-001");

        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
        Assert.Equal("printer-01", batch.PrinterId);
        Assert.Equal("job-001", batch.PrintJobId);
    }

    [Fact]
    public void A_different_mes_association_cannot_replace_the_committed_fact()
    {
        var batch = Create(PlainRule("code128"), "{}", ["00000000001"]);
        batch.Activate("report-id-001", "PR-001");

        Assert.Throws<InvalidOperationException>(() => batch.Activate("report-id-002", "PR-002"));

        Assert.Equal("ready-to-print", batch.Status);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
    }

    [Fact]
    public void Controlled_retry_reuses_the_activated_batch_and_allocated_serial()
    {
        var batch = Create(PlainRule("code128"), "{}", ["00000000001"]);
        var batchId = batch.Id;
        var itemId = batch.Items.Single().Id;
        var serialNumber = batch.Items.Single().SerialNumber;
        batch.Activate("report-id-001", "PR-001");
        batch.RecordPrintFailed("printer-01", "transport unavailable");

        batch.EnsureCanBeDispatched();
        batch.RecordSentToPrinter("printer-01", "job-retry-001");

        Assert.Equal(batchId, batch.Id);
        Assert.Equal(itemId, batch.Items.Single().Id);
        Assert.Equal(serialNumber, batch.Items.Single().SerialNumber);
        Assert.Equal("sent-to-printer", batch.Status);
    }

    [Fact]
    public void Allocated_gs1_batch_uses_server_serials_instead_of_caller_serial_prefix()
    {
        var batch = Create(
            Gs1Rule(),
            """{"lotNo":"LOT-A","serialPrefix":"CALLER-CONTROLLED-"}""",
            ["00000000001", "00000000002"]);

        Assert.Equal(["00000000001", "00000000002"], batch.Items.Select(item => item.SerialNumber!).ToArray());
        Assert.All(batch.Items, item =>
        {
            Assert.Equal("09506000134352", item.Gtin);
            Assert.Equal("LOT-A", item.LotNo);
            Assert.Contains($"(21){item.SerialNumber}", item.LabelValue, StringComparison.Ordinal);
            Assert.DoesNotContain("CALLER-CONTROLLED-", item.LabelValue, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Allocated_batch_requires_one_distinct_serial_per_requested_item()
    {
        Assert.Throws<ArgumentException>(() => Create(PlainRule("code128"), "{}", ["00000000001"], 2));
        Assert.Throws<ArgumentException>(() => Create(PlainRule("code128"), "{}", ["00000000001", "00000000001"]));
    }

    private static LabelPrintBatch Create(
        BarcodeRule rule,
        string labelValuesJson,
        IReadOnlyList<string> serialNumbers,
        int? requestedQuantity = null) =>
        LabelPrintBatch.CreateWithAllocatedSerialNumbers(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            new LabelPrintBatchSnapshot(
                "file-template-001",
                $"sha256:{new string('a', 64)}",
                """{"version":1,"variables":[]}""",
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            "idem-print-001",
            labelValuesJson,
            requestedQuantity ?? serialNumbers.Count,
            serialNumbers);

    private static BarcodeRule PlainRule(string barcodeType) =>
        BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG",
            barcodeType,
            "FG",
            40,
            "none",
            ["wms.inbound"],
            "active");

    private static BarcodeRule Gs1Rule() =>
        BarcodeRule.Create(
            "org-001",
            "env-dev",
            "GS1-FG",
            "gs1-128",
            "0950600013435",
            80,
            "gs1-mod10",
            ["wms.inbound"],
            "active",
            7);
}
