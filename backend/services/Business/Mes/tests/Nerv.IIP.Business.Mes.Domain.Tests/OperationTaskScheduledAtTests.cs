using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

// scheduled_at_utc is the schedule-specific fact used to derive 已排程/未排程 and is the reason the migration
// deliberately does NOT backfill legacy rows (assigned_at_utc cannot tell an APS placement from a manual
// dispatch). These tests pin the forward guarantee: only a released schedule assignment writes it, manual
// dispatch never does — including the two migration counterexamples (a null-operator manual dispatch, and a
// manual-dispatch-then-reschedule row).
public sealed class OperationTaskScheduledAtTests
{
    private static readonly DateTimeOffset EarliestStart = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static OperationTask Queue() => OperationTask.Queue(
        "org-001", "env-dev", "WO-01", "OP-10", 10, "WC-1", [], EarliestStart, TimeSpan.FromMinutes(30));

    [Fact]
    public void Manual_dispatch_never_sets_scheduled_at()
    {
        var task = Queue();
        task.Assign("operator-1", "DEV-1", "shift-a", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Null(task.ScheduledAtUtc);
    }

    [Fact]
    public void Manual_dispatch_with_null_operator_never_sets_scheduled_at()
    {
        // Counterexample (1): Assign may be called with a null operator (manual device-only dispatch). Such a row
        // has assigned_user_id IS NULL yet was never scheduled, so it must not be treated as APS-placed.
        var task = Queue();
        task.Assign(null, "DEV-1", "shift-a", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Null(task.AssignedUserId);
        Assert.Null(task.ScheduledAtUtc);
    }

    [Fact]
    public void Released_schedule_assignment_sets_scheduled_at()
    {
        var scheduledAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var task = Queue();
        task.ApplyScheduleAssignment("WC-1", "DEV-1", scheduledAt, scheduledAt.AddMinutes(30), scheduledAt);

        Assert.Equal(scheduledAt, task.ScheduledAtUtc);
    }

    [Fact]
    public void Manual_dispatch_then_released_schedule_reconciles_scheduled_at()
    {
        // Counterexample (2): a real dispatch→reschedule row keeps its operator (ApplyScheduleAssignment does not
        // clear assigned_user_id), yet it IS scheduled — the schedule assignment reconciles scheduled_at_utc.
        var scheduledAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var task = Queue();
        task.Assign("operator-1", "DEV-1", "shift-a", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        task.ApplyScheduleAssignment("WC-1", "DEV-1", scheduledAt, scheduledAt.AddMinutes(30), scheduledAt);

        Assert.Equal("operator-1", task.AssignedUserId);
        Assert.Equal(scheduledAt, task.ScheduledAtUtc);
    }
}
