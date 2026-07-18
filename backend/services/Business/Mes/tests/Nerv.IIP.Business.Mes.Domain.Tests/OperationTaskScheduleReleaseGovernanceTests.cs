using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class OperationTaskScheduleReleaseGovernanceTests
{
    [Fact]
    public void Newer_schedule_release_wins_and_late_old_revoke_is_ignored()
    {
        var task = CreateTask();

        task.ApplyScheduleAssignment("WC-1", "DEV-1", At(1), At(2), At(0), schedulePlanId: "plan-1", scheduleReleaseRevision: 1);
        task.ApplyScheduleAssignment("WC-2", "DEV-2", At(3), At(4), At(2), schedulePlanId: "plan-2", scheduleReleaseRevision: 2);

        task.RevokeScheduleAssignment("plan-1", 1, "superseded");

        Assert.Equal("plan-2", task.SchedulePlanId);
        Assert.Equal(2, task.ScheduleReleaseRevision);
        Assert.Equal("DEV-2", task.DeviceAssetId);
        Assert.Equal(OperationTaskLifecycleStatus.Queued, task.Status);
    }

    [Fact]
    public void Matching_revoke_clears_schedule_provenance_and_is_idempotent()
    {
        var task = CreateTask();
        task.ApplyScheduleAssignment("WC-1", "DEV-1", At(1), At(2), At(0), schedulePlanId: "plan-1", scheduleReleaseRevision: 1);

        task.RevokeScheduleAssignment("plan-1", 1, "explicit");
        task.RevokeScheduleAssignment("plan-1", 1, "explicit");

        Assert.Null(task.SchedulePlanId);
        Assert.Null(task.ScheduleReleaseRevision);
        Assert.Null(task.ScheduledAtUtc);
        Assert.Null(task.DeviceAssetId);
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
        Assert.Equal("explicit", task.ScheduleInvalidationReasonCode);
    }

    [Fact]
    public void Matching_revoke_preserves_active_manual_dispatch()
    {
        var task = CreateTask();
        task.Assign("operator-1", "DEV-MANUAL", "shift-a", At(0), "user:operator-1");
        task.ApplyScheduleAssignment("WC-APS", "DEV-APS", At(1), At(2), At(0), schedulePlanId: "plan-1", scheduleReleaseRevision: 1);

        task.RevokeScheduleAssignment("plan-1", 1, "explicit");

        Assert.Equal("DEV-MANUAL", task.DeviceAssetId);
        Assert.True(task.HasActiveManualDispatch);
        Assert.Null(task.SchedulePlanId);
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
    }

    private static OperationTask CreateTask() => OperationTask.Queue(
        "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-OLD", [], At(0), TimeSpan.FromHours(1));

    private static DateTimeOffset At(int hour) => DateTimeOffset.Parse("2026-07-18T00:00:00Z").AddHours(hour);
}
