using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

public sealed class OperationExecutionProjectionTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Older_start_fills_actual_start_without_regressing_newer_paused_state()
    {
        var projection = CreateProjection();

        projection.ApplyPaused(BaseTime.AddMinutes(20), "evt-pause");
        projection.ApplyStarted(BaseTime.AddMinutes(10), "evt-start");

        Assert.Equal(BaseTime.AddMinutes(10), projection.ActualStartedAtUtc);
        Assert.True(projection.IsPaused);
        Assert.Equal(BaseTime.AddMinutes(20), projection.LifecycleOccurredAtUtc);
        Assert.Equal("evt-pause", projection.LifecycleEventId);
        Assert.Equal(BaseTime.AddMinutes(20), projection.LatestSourceOccurredAtUtc);
        Assert.Equal("evt-pause", projection.LatestSourceEventId);
    }

    [Fact]
    public void Newer_lifecycle_facts_resume_and_complete_the_operation()
    {
        var projection = CreateProjection();

        projection.ApplyStarted(BaseTime, "evt-start");
        projection.ApplyPaused(BaseTime.AddMinutes(10), "evt-pause");
        projection.ApplyResumed(BaseTime.AddMinutes(20), "evt-resume");
        projection.ApplyCompleted(BaseTime.AddMinutes(30), "evt-complete");

        Assert.Equal(BaseTime, projection.ActualStartedAtUtc);
        Assert.Equal(BaseTime.AddMinutes(30), projection.ActualCompletedAtUtc);
        Assert.False(projection.IsPaused);
        Assert.Equal("evt-complete", projection.LatestSourceEventId);
    }

    [Fact]
    public void Production_report_deltas_keep_net_good_quantity()
    {
        var projection = CreateProjection();

        projection.AddCompletedQuantity(12m, BaseTime.AddMinutes(5), "evt-report");
        projection.AddCompletedQuantity(-4m, BaseTime.AddMinutes(15), "evt-reversal");

        Assert.Equal(8m, projection.CompletedQuantity);
        Assert.Equal("evt-reversal", projection.LatestSourceEventId);
    }

    [Fact]
    public void Downtime_and_quality_use_independent_watermarks()
    {
        var projection = CreateProjection();

        projection.ApplyDowntimeStarted("DT-001", BaseTime.AddMinutes(30), "evt-downtime");
        projection.ApplyQualityReleased(BaseTime.AddMinutes(40), "evt-quality-release");
        projection.ApplyDowntimeRestored("DT-001", BaseTime.AddMinutes(20), "evt-old-restore");
        projection.ApplyQualityBlocked(BaseTime.AddMinutes(35), "evt-old-reject");

        Assert.True(projection.IsDowntimeBlocked);
        Assert.False(projection.IsQualityBlocked);
        Assert.Equal(BaseTime.AddMinutes(30), projection.DowntimeOccurredAtUtc);
        Assert.Equal(BaseTime.AddMinutes(40), projection.QualityOccurredAtUtc);
        Assert.Equal("evt-quality-release", projection.LatestSourceEventId);
    }

    [Fact]
    public void Earlier_first_fact_on_another_axis_is_not_rejected_by_the_global_latest_source()
    {
        var projection = CreateProjection();

        projection.ApplyDowntimeStarted("DT-001", BaseTime.AddMinutes(30), "evt-downtime");
        projection.ApplyQualityBlocked(BaseTime.AddMinutes(20), "evt-quality");

        Assert.True(projection.IsDowntimeBlocked);
        Assert.True(projection.IsQualityBlocked);
        Assert.Equal(BaseTime.AddMinutes(30), projection.LatestSourceOccurredAtUtc);
    }

    [Fact]
    public void Restoring_one_of_two_active_downtimes_keeps_the_operation_blocked()
    {
        var projection = CreateProjection();

        projection.ApplyDowntimeStarted("DT-A", BaseTime.AddMinutes(10), "evt-a-start");
        projection.ApplyDowntimeStarted("DT-B", BaseTime.AddMinutes(20), "evt-b-start");
        projection.ApplyDowntimeRestored("DT-A", BaseTime.AddMinutes(30), "evt-a-restored");

        Assert.True(projection.IsDowntimeBlocked);

        projection.ApplyDowntimeRestored("DT-B", BaseTime.AddMinutes(40), "evt-b-restored");

        Assert.False(projection.IsDowntimeBlocked);
    }

    [Fact]
    public void Zero_duration_downtime_converges_to_restored_when_events_arrive_in_reverse_order()
    {
        var projection = CreateProjection();
        var occurredAtUtc = BaseTime.AddMinutes(10);

        projection.ApplyDowntimeRestored("DT-001", occurredAtUtc, "evt-restored");
        projection.ApplyDowntimeStarted("DT-001", occurredAtUtc, "evt-started");

        Assert.False(projection.IsDowntimeBlocked);
        Assert.Equal("evt-restored", projection.DowntimeEventId);
    }

    [Fact]
    public void Zero_duration_downtime_converges_to_restored_when_events_arrive_in_source_order()
    {
        var projection = CreateProjection();
        var occurredAtUtc = BaseTime.AddMinutes(10);

        projection.ApplyDowntimeStarted("DT-001", occurredAtUtc, "evt-started");
        projection.ApplyDowntimeRestored("DT-001", occurredAtUtc, "evt-restored");

        Assert.False(projection.IsDowntimeBlocked);
        Assert.Equal("evt-restored", projection.DowntimeEventId);
    }

    private static OperationExecutionProjection CreateProjection() =>
        OperationExecutionProjection.Create(
            "org-001",
            "env-dev",
            "wo-001",
            "op-010",
            operationSequence: 10,
            workCenterId: "wc-001",
            BaseTime,
            "evt-created");
}
