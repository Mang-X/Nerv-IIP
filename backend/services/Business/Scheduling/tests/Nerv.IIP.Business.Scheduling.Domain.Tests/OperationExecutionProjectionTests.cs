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

        projection.ApplyDowntimeStarted(BaseTime.AddMinutes(30), "evt-downtime");
        projection.ApplyQualityReleased(BaseTime.AddMinutes(40), "evt-quality-release");
        projection.ApplyDowntimeRestored(BaseTime.AddMinutes(20), "evt-old-restore");
        projection.ApplyQualityBlocked(BaseTime.AddMinutes(35), "evt-old-reject");

        Assert.True(projection.IsDowntimeBlocked);
        Assert.False(projection.IsQualityBlocked);
        Assert.Equal(BaseTime.AddMinutes(30), projection.DowntimeOccurredAtUtc);
        Assert.Equal(BaseTime.AddMinutes(40), projection.QualityOccurredAtUtc);
        Assert.Equal("evt-quality-release", projection.LatestSourceEventId);
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
