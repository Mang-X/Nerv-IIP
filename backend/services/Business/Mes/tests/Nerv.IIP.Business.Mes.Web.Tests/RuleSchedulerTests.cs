using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class RuleSchedulerTests
{
    [Fact]
    public void Schedule_WhenPrimaryWorkCenterUnavailable_ShiftsOperationAfterRestoration()
    {
        var scheduler = new RuleScheduler();
        var start = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var due = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        var result = scheduler.Schedule(
            [
                new ScheduleOperation(
                    "WO-001",
                    "OP-10",
                    OperationTaskStatus.Queued,
                    10,
                    100,
                    due,
                    start,
                    TimeSpan.FromHours(2),
                    "WC-A",
                    []),
            ],
            [
                new WorkCenterUnavailability("WC-A", start.AddHours(1), start.AddHours(3), "breakdown"),
            ]);

        var assignment = Assert.Single(result.Assignments);
        Assert.Equal("WC-A", assignment.WorkCenterId);
        Assert.Equal(start.AddHours(3), assignment.StartUtc);
        Assert.Equal(start.AddHours(5), assignment.EndUtc);
        Assert.Equal("rule-sequenced", assignment.Reason);
    }

    [Fact]
    public void Schedule_WhenAlternativeWorkCenterAvailable_UsesAlternativeInsteadOfWaiting()
    {
        var scheduler = new RuleScheduler();
        var start = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var due = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        var result = scheduler.Schedule(
            [
                new ScheduleOperation(
                    "WO-002",
                    "OP-10",
                    OperationTaskStatus.Queued,
                    10,
                    100,
                    due,
                    start,
                    TimeSpan.FromHours(2),
                    "WC-A",
                    ["WC-B"]),
            ],
            [
                new WorkCenterUnavailability("WC-A", start, start.AddHours(4), "maintenance"),
            ]);

        var assignment = Assert.Single(result.Assignments);
        Assert.Equal("WC-B", assignment.WorkCenterId);
        Assert.Equal(start, assignment.StartUtc);
        Assert.Equal(start.AddHours(2), assignment.EndUtc);
    }

    [Fact]
    public void Schedule_WhenOperationInProgress_PreservesExistingAssignment()
    {
        var scheduler = new RuleScheduler();
        var start = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var due = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

        var result = scheduler.Schedule(
            [
                new ScheduleOperation(
                    "WO-STARTED",
                    "OP-10",
                    OperationTaskStatus.InProgress,
                    10,
                    100,
                    due,
                    start,
                    TimeSpan.FromHours(2),
                    "WC-A",
                    [],
                    start,
                    start.AddHours(2)),
                new ScheduleOperation(
                    "WO-QUEUED",
                    "OP-10",
                    OperationTaskStatus.Queued,
                    10,
                    50,
                    due,
                    start,
                    TimeSpan.FromHours(1),
                    "WC-A",
                    []),
            ],
            [
                new WorkCenterUnavailability("WC-A", start, start.AddHours(4), "breakdown"),
            ]);

        var preserved = Assert.Single(result.Assignments, x => x.WorkOrderId == "WO-STARTED");
        Assert.Equal(start, preserved.StartUtc);
        Assert.Equal(start.AddHours(2), preserved.EndUtc);
        Assert.Equal("in-progress-preserved", preserved.Reason);

        var rescheduled = Assert.Single(result.Assignments, x => x.WorkOrderId == "WO-QUEUED");
        Assert.Equal(start.AddHours(4), rescheduled.StartUtc);
    }
}
