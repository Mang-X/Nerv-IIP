using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class OperationTaskScheduleStateTests
{
    private static OperationTask CreateQueued() => OperationTask.Queue(
        "org-001",
        "env-dev",
        "WO-001",
        "WO-001-OP-10",
        10,
        "WC-OLD",
        [],
        DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
        TimeSpan.FromMinutes(30));

    [Fact]
    public void Newly_queued_task_has_no_scheduled_at_utc()
    {
        var task = CreateQueued();

        Assert.Null(task.ScheduledAtUtc);
        Assert.Equal(OperationTaskLifecycleStatus.Queued, task.Status);
    }

    [Fact]
    public void Manual_dispatch_assign_does_not_set_scheduled_at_utc()
    {
        var task = CreateQueued();

        // Manual dispatch records assignment facts but must NOT count as "scheduled": only a released
        // APS plan (ApplyScheduleAssignment) may set ScheduledAtUtc.
        task.Assign("operator-001", "DEV-OLD-01", "shift-a", DateTimeOffset.Parse("2026-06-01T07:45:00Z"));

        Assert.NotNull(task.AssignedAtUtc);
        Assert.Null(task.ScheduledAtUtc);
    }

    [Fact]
    public void Released_schedule_assignment_sets_scheduled_at_utc()
    {
        var task = CreateQueued();

        task.ApplyScheduleAssignment(
            "WC-OIL",
            "DEV-OIL-01",
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-01T13:30:00Z"),
            DateTimeOffset.Parse("2026-06-01T07:30:00Z"),
            "STD-OIL");

        Assert.Equal(DateTimeOffset.Parse("2026-06-01T07:30:00Z"), task.ScheduledAtUtc);
    }

    [Fact]
    public void Schedule_invalidated_task_cannot_be_manually_dispatched()
    {
        var task = CreateQueued();
        task.MarkScheduleInvalidated("equipmentUnavailable");

        var exception = Assert.Throws<KnownException>(() =>
            task.Assign("operator-001", "DEV-OIL-01", "shift-a", DateTimeOffset.Parse("2026-06-01T08:00:00Z")));
        Assert.Contains("rescheduled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
        Assert.Null(task.AssignedUserId);
    }
}
