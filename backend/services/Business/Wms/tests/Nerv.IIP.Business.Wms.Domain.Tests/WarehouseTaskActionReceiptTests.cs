using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WarehouseTaskActionReceiptTests
{
    [Fact]
    public void Receipt_persists_authoritative_action_result_and_matches_same_payload()
    {
        var taskId = new WarehouseTaskId(Guid.CreateVersion7());

        var receipt = WarehouseTaskActionReceipt.Create(
            " org-001 ",
            " env-dev ",
            taskId,
            " progress ",
            " idem-progress-001 ",
            " sha256:payload-001 ",
            " InProgress ",
            3,
            2.5m,
            2.5m);

        Assert.Equal("org-001", receipt.OrganizationId);
        Assert.Equal("env-dev", receipt.EnvironmentId);
        Assert.Equal(taskId, receipt.WarehouseTaskId);
        Assert.Equal("progress", receipt.Action);
        Assert.Equal("idem-progress-001", receipt.IdempotencyKey);
        Assert.Equal("sha256:payload-001", receipt.PayloadFingerprint);
        Assert.Equal("InProgress", receipt.ResultStatus);
        Assert.Equal(3, receipt.ResultVersion);
        Assert.Equal(2.5m, receipt.ResultExecutedQuantity);
        Assert.Equal(2.5m, receipt.ResultDifferenceQuantity);
        Assert.True(receipt.MatchesPayload(" sha256:payload-001 "));
        receipt.EnsurePayloadMatches("sha256:payload-001");
    }

    [Fact]
    public void Receipt_rejects_same_key_replay_with_a_different_payload()
    {
        var receipt = WarehouseTaskActionReceipt.Create(
            "org-001",
            "env-dev",
            new WarehouseTaskId(Guid.CreateVersion7()),
            "complete",
            "idem-complete-001",
            "sha256:payload-001",
            "Completed",
            4,
            5m,
            0m);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            receipt.EnsurePayloadMatches("sha256:payload-002"));

        Assert.False(receipt.MatchesPayload("sha256:payload-002"));
        Assert.Contains("payload", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
