using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class LabelPrintBatchReservationTests
{
    [Fact]
    public void Reserved_gs1_batch_uses_the_allocated_serials_in_sequence_order()
    {
        var batch = Reserve(
            Gs1Rule(),
            """{"skuCode":"SKU-FG-1000","lotNo":"LOT-A","serialPrefix":"CALLER-CONTROLLED-"}""",
            ["00000000001", "00000000002"]);

        Assert.Equal("reserved", batch.Status);
        Assert.Equal([1, 2], batch.Items.Select(item => item.SequenceNo).ToArray());
        Assert.Equal(["00000000001", "00000000002"], batch.Items.Select(item => item.SerialNumber!).ToArray());
        Assert.All(batch.Items, item =>
        {
            Assert.Equal("09506000134352", item.Gtin);
            Assert.Equal("LOT-A", item.LotNo);
            Assert.Contains($"(21){item.SerialNumber}", item.LabelValue, StringComparison.Ordinal);
            Assert.EndsWith($".{item.SerialNumber}", item.EpcUri, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("code128")]
    [InlineData("qr")]
    [InlineData("datamatrix")]
    public void Reserved_plain_batch_exposes_each_allocated_serial(string barcodeType)
    {
        var rule = BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG",
            barcodeType,
            "FG",
            40,
            "none",
            ["wms.inbound"],
            "active");

        var batch = Reserve(rule, "{}", ["00000000001", "00000000002"]);

        Assert.Equal(["00000000001", "00000000002"], batch.Items.Select(item => item.SerialNumber!).ToArray());
        Assert.Equal(["FG00000000001", "FG00000000002"], batch.Items.Select(item => item.LabelValue).ToArray());
        Assert.All(batch.Items, item =>
        {
            Assert.Null(item.Gtin);
            Assert.Null(item.LotNo);
            Assert.Null(item.EpcUri);
        });
    }

    [Fact]
    public void Reserved_plain_batch_fits_allocated_serial_into_an_existing_thirteen_character_rule()
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
        var serialNumber = LabelSerialNumber.Format(rule.Id, 1, rule.AllocatedSerialNumberLength);

        var batch = Reserve(rule, "{}", [serialNumber]);

        Assert.Equal(13, batch.Items.Single().LabelValue.Length);
        Assert.Equal(serialNumber, batch.Items.Single().SerialNumber);
    }

    [Fact]
    public void Reserved_batch_is_not_dispatchable_until_mes_association_is_activated()
    {
        var batch = Reserve(PlainRule(), "{}", ["00000000001"]);

        Assert.Throws<LabelPrintLifecycleRejectedException>(() => batch.EnsureCanBeDispatched());

        batch.Activate("report-id-001", "PR-001");
        batch.EnsureCanBeDispatched();

        Assert.Equal("ready-to-print", batch.Status);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
    }

    [Fact]
    public void Activation_is_idempotent_for_the_same_mes_association_and_rejects_a_different_one()
    {
        var batch = Reserve(PlainRule(), "{}", ["00000000001"]);

        batch.Activate("report-id-001", "PR-001");
        batch.Activate("report-id-001", "PR-001");

        Assert.Throws<InvalidOperationException>(() => batch.Activate("report-id-002", "PR-002"));
    }

    [Fact]
    public void Reservation_requires_one_unique_serial_per_requested_item()
    {
        Assert.Throws<ArgumentException>(() => Reserve(PlainRule(), "{}", ["00000000001"] , requestedQuantity: 2));
        Assert.Throws<ArgumentException>(() => Reserve(PlainRule(), "{}", ["00000000001", "00000000001"]));
    }

    private static LabelPrintBatch Reserve(
        BarcodeRule rule,
        string labelValuesJson,
        IReadOnlyList<string> serialNumbers,
        int? requestedQuantity = null) =>
        LabelPrintBatch.Reserve(
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

    private static BarcodeRule PlainRule() =>
        BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG",
            "code128",
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
