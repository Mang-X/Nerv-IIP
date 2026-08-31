using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class LabelPrintBatchSnapshotTests
{
    [Fact]
    public void New_batch_freezes_complete_replay_facts_and_idempotency_compares_them()
    {
        var rule = ActiveRule();
        var templateId = new LabelTemplateId(Guid.CreateVersion7());
        var snapshot = ReplaySnapshot();

        var batch = NewBatch(rule, templateId, snapshot);
        var same = NewBatch(rule, templateId, snapshot);
        var changedAsset = NewBatch(
            rule,
            templateId,
            snapshot with { TemplateAssetSha256 = $"sha256:{new string('b', 64)}" });

        Assert.Equal(snapshot.TemplateFileId, batch.TemplateFileIdSnapshot);
        Assert.Equal(snapshot.TemplateAssetSha256, batch.TemplateAssetSha256);
        Assert.Equal(snapshot.VariableSchemaJson, batch.VariableSchemaJsonSnapshot);
        Assert.Equal(snapshot.BarcodeType, batch.BarcodeTypeSnapshot);
        Assert.Equal(snapshot.RendererContractVersion, batch.RendererContractVersion);
        Assert.True(batch.HasSameIdempotencyPayload(same));
        Assert.False(batch.HasSameIdempotencyPayload(changedAsset));
    }

    [Fact]
    public void New_batch_rejects_incomplete_or_rule_mismatched_replay_facts()
    {
        var rule = ActiveRule();
        var templateId = new LabelTemplateId(Guid.CreateVersion7());
        var snapshot = ReplaySnapshot();
        var invalidSnapshots = new[]
        {
            snapshot with { TemplateFileId = " " },
            snapshot with { TemplateAssetSha256 = " " },
            snapshot with { VariableSchemaJson = " " },
            snapshot with { BarcodeType = " " },
            snapshot with { RendererContractVersion = " " },
            snapshot with { BarcodeType = "qr" },
        };

        Assert.All(invalidSnapshots, value =>
            Assert.Throws<ArgumentException>(() => NewBatch(rule, templateId, value)));
    }

    [Fact]
    public void Legacy_batch_remains_identifiable_without_invented_replay_facts()
    {
        var batch = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot(
            "org-001",
            "env-dev",
            ActiveRule(),
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-legacy-001",
            "{}",
            1);

        Assert.Null(batch.TemplateFileIdSnapshot);
        Assert.Null(batch.TemplateAssetSha256);
        Assert.Null(batch.VariableSchemaJsonSnapshot);
        Assert.Null(batch.BarcodeTypeSnapshot);
        Assert.Null(batch.RendererContractVersion);
    }

    private static BarcodeRule ActiveRule() =>
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

    private static LabelPrintBatchSnapshot ReplaySnapshot() =>
        new(
            "file-template-001",
            $"sha256:{new string('a', 64)}",
            """{"version":1,"variables":[]}""",
            "code128",
            ZplV1LabelCompiler.ContractVersion);

    private static LabelPrintBatch NewBatch(
        BarcodeRule rule,
        LabelTemplateId templateId,
        LabelPrintBatchSnapshot snapshot) =>
        LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            templateId,
            snapshot,
            "wms.inbound",
            "ASN-001",
            "idem-print-001",
            "{}",
            1);
}
