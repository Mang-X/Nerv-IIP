using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class OperationTaskClaimTests
{
    [Fact]
    public void Self_claim_rejects_a_task_already_claimed_by_another_worker()
    {
        var task = NewTask("OP-001");
        task.Claim("worker-a", "操作员甲", null, "SHIFT-A", At(1), "user:worker-a", "TEAM-A", "甲班");

        var exception = Assert.Throws<KnownException>(() => task.Claim(
            "worker-b", "操作员乙", null, "SHIFT-B", At(2), "user:worker-b", "TEAM-B", "乙班"));

        Assert.Contains("已被领取", exception.Message, StringComparison.Ordinal);
        Assert.Equal("worker-a", task.AssignedUserId);
        Assert.Equal("操作员甲", task.AssignedUserName);
        Assert.Equal(At(1), task.AssignedAtUtc);
    }

    [Fact]
    public void Self_claim_rejects_a_task_that_is_no_longer_pending()
    {
        var task = NewTask("OP-002");
        task.Start(At(1));

        var exception = Assert.Throws<KnownException>(() => task.Claim(
            "worker-a", "操作员甲", null, "SHIFT-A", At(2), "user:worker-a"));

        Assert.Contains("待领取", exception.Message, StringComparison.Ordinal);
        Assert.Null(task.AssignedUserId);
    }

    private static OperationTask NewTask(string operationTaskId) => OperationTask.Queue(
        "org-001", "env-dev", "WO-001", operationTaskId, 10, "WC-001", [], At(0), TimeSpan.FromHours(1));

    private static DateTimeOffset At(int minute) =>
        DateTimeOffset.Parse("2026-08-30T08:00:00Z").AddMinutes(minute);
}
