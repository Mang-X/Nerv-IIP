using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class PeriodicInspectionOperationTests
{
    private static readonly DateTime ReleasedAtUtc = new(2026, 8, 24, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Report_before_release_is_reconciled_into_the_frozen_plan_context()
    {
        var operation = PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-001", "OP-001");
        var reportedAtUtc = ReleasedAtUtc.AddMinutes(15);

        Assert.True(operation.RecordProductionReport(
            "RPT-001", "WC-001", 40m, "EA", reportedAtUtc, isReversal: false, reversedReportNo: null));

        operation.ApplyRelease(
            "SKU-FG-1000",
            operationSequence: 10,
            "WC-001",
            ReleasedAtUtc,
            [PeriodicInspectionPlanSnapshot.From(NewPeriodicPlan())]);

        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(ReleasedAtUtc.AddHours(2).AddMinutes(15), context.NextTimeWindowAtUtc);
        Assert.Equal("SKU-FG-1000", context.SkuCode);
        Assert.Equal("WC-001", context.WorkCenterId);
        Assert.Equal(1, context.InspectionPlanVersion);
        Assert.Equal(2m, context.TimeIntervalHours);
        Assert.Equal(100m, context.QuantityInterval);
        Assert.Equal("team-quality-001", context.AssignedTeamId);
        Assert.Equal(reportedAtUtc, context.FirstActivityAtUtc);
        Assert.Equal(40m, context.CumulativeGoodQuantity);
        Assert.Equal(40m, context.QuantityHighWater);
    }

    [Fact]
    public void Reversal_reduces_net_quantity_without_advancing_or_rolling_back_the_high_water()
    {
        var operation = ReleasedOperation();

        operation.RecordProductionReport("RPT-001", "WC-001", 100m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        operation.RecordProductionReport("RPT-002", "WC-001", -30m, "EA", ReleasedAtUtc.AddMinutes(20), true, "RPT-001");
        operation.RecordProductionReport("RPT-003", "WC-001", 20m, "EA", ReleasedAtUtc.AddMinutes(5), false, null);

        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(90m, context.CumulativeGoodQuantity);
        Assert.Equal(120m, context.QuantityHighWater);
        Assert.Equal(ReleasedAtUtc.AddMinutes(5), context.FirstActivityAtUtc);
    }

    [Fact]
    public void Quantity_window_is_not_generated_before_the_first_frozen_interval_is_reached()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 99.999999m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        var context = Assert.Single(operation.RuntimeContexts);

        Assert.Empty(context.TakeDueQuantityWindows());
        Assert.Equal(0, context.LastGeneratedQuantityWindowSequence);
    }

    [Fact]
    public void One_report_crossing_multiple_quantity_intervals_emits_each_cumulative_threshold()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 250m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        var context = Assert.Single(operation.RuntimeContexts);

        var windows = context.TakeDueQuantityWindows();

        Assert.Collection(
            windows,
            window => Assert.Equal((1L, 100m), (window.Sequence, window.ThresholdQuantity)),
            window => Assert.Equal((2L, 200m), (window.Sequence, window.ThresholdQuantity)));
        Assert.Equal(2, context.LastGeneratedQuantityWindowSequence);
    }

    [Fact]
    public void Quantity_remainder_continues_from_the_persisted_window_sequence()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 250m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(2, context.TakeDueQuantityWindows().Count);

        operation.RecordProductionReport("RPT-002", "WC-001", 49.999999m, "EA", ReleasedAtUtc.AddMinutes(20), false, null);
        Assert.Empty(context.TakeDueQuantityWindows());

        operation.RecordProductionReport("RPT-003", "WC-001", 0.000001m, "EA", ReleasedAtUtc.AddMinutes(30), false, null);
        var next = Assert.Single(context.TakeDueQuantityWindows());

        Assert.Equal((3L, 300m), (next.Sequence, next.ThresholdQuantity));
    }

    [Fact]
    public void Reversal_neither_generates_nor_reclaims_quantity_windows()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 200m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(2, context.TakeDueQuantityWindows().Count);

        operation.RecordProductionReport("RPT-REV", "WC-001", -150m, "EA", ReleasedAtUtc.AddMinutes(20), true, "RPT-001");

        Assert.Empty(context.TakeDueQuantityWindows());
        Assert.Equal(2, context.LastGeneratedQuantityWindowSequence);
        Assert.Equal(50m, context.CumulativeGoodQuantity);
        Assert.Equal(200m, context.QuantityHighWater);
    }

    [Fact]
    public void Closed_context_does_not_generate_unclaimed_quantity_windows()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 250m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        operation.Complete("SKU-FG-1000", 10, "WC-001", "EA", ReleasedAtUtc.AddHours(3));
        var context = Assert.Single(operation.RuntimeContexts);

        Assert.Empty(context.TakeDueQuantityWindows());
        Assert.Equal(0, context.LastGeneratedQuantityWindowSequence);
    }

    [Fact]
    public void Later_arriving_report_with_later_business_time_does_not_replace_first_activity()
    {
        var operation = ReleasedOperation();

        operation.RecordProductionReport("RPT-001", "WC-001", 10m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        operation.RecordProductionReport("RPT-002", "WC-001", 20m, "EA", ReleasedAtUtc.AddMinutes(40), false, null);

        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(ReleasedAtUtc.AddMinutes(10), context.FirstActivityAtUtc);
    }

    [Fact]
    public void Time_windows_start_one_interval_after_first_production_activity_and_advance_once()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 40m, "EA", ReleasedAtUtc.AddMinutes(15), false, null);
        var context = Assert.Single(operation.RuntimeContexts);

        Assert.Empty(context.TakeDueTimeWindows(ReleasedAtUtc.AddHours(2).AddMinutes(14), maxWindows: 24));

        var first = Assert.Single(context.TakeDueTimeWindows(ReleasedAtUtc.AddHours(2).AddMinutes(15), maxWindows: 24));
        Assert.Equal(1, first.Sequence);
        Assert.Equal(ReleasedAtUtc.AddHours(2).AddMinutes(15), first.DueAtUtc);

        Assert.Empty(context.TakeDueTimeWindows(ReleasedAtUtc.AddHours(2).AddMinutes(15), maxWindows: 24));
        Assert.Equal(1, context.LastGeneratedTimeWindowSequence);
        Assert.Equal(ReleasedAtUtc.AddHours(4).AddMinutes(15), context.NextTimeWindowAtUtc);
    }

    [Fact]
    public void Time_window_catch_up_is_bounded_and_keeps_the_frozen_anchor_after_generation()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 40m, "EA", ReleasedAtUtc.AddHours(1), false, null);
        var context = Assert.Single(operation.RuntimeContexts);

        var firstBatch = context.TakeDueTimeWindows(ReleasedAtUtc.AddHours(8), maxWindows: 2);

        Assert.Collection(
            firstBatch,
            window => Assert.Equal((1L, ReleasedAtUtc.AddHours(3)), (window.Sequence, window.DueAtUtc)),
            window => Assert.Equal((2L, ReleasedAtUtc.AddHours(5)), (window.Sequence, window.DueAtUtc)));

        operation.RecordProductionReport("RPT-LATE", "WC-001", 10m, "EA", ReleasedAtUtc.AddMinutes(30), false, null);
        var secondBatch = context.TakeDueTimeWindows(ReleasedAtUtc.AddHours(8), maxWindows: 2);

        Assert.Single(secondBatch);
        Assert.Equal((3L, ReleasedAtUtc.AddHours(7)), (secondBatch[0].Sequence, secondBatch[0].DueAtUtc));
        Assert.Equal(ReleasedAtUtc.AddHours(1), context.TimeScheduleAnchorAtUtc);
        Assert.Equal(ReleasedAtUtc.AddHours(9), context.NextTimeWindowAtUtc);
    }

    [Fact]
    public void Time_windows_are_not_generated_without_activity_or_after_context_closes()
    {
        var operation = ReleasedOperation();
        var context = Assert.Single(operation.RuntimeContexts);

        Assert.Empty(context.TakeDueTimeWindows(ReleasedAtUtc.AddDays(1), maxWindows: 24));

        operation.RecordProductionReport("RPT-001", "WC-001", 40m, "EA", ReleasedAtUtc.AddMinutes(15), false, null);
        operation.Complete("SKU-FG-1000", 10, "WC-001", "EA", ReleasedAtUtc.AddHours(3));

        Assert.Empty(context.TakeDueTimeWindows(ReleasedAtUtc.AddDays(1), maxWindows: 24));
        Assert.Null(context.NextTimeWindowAtUtc);
    }

    [Fact]
    public void Closed_status_fails_closed_even_if_a_malformed_persisted_row_retains_a_due_watermark()
    {
        var operation = ReleasedOperation();
        operation.RecordProductionReport("RPT-001", "WC-001", 40m, "EA", ReleasedAtUtc.AddMinutes(15), false, null);
        var context = Assert.Single(operation.RuntimeContexts);
        typeof(PeriodicInspectionRuntimeContext)
            .GetProperty(nameof(PeriodicInspectionRuntimeContext.Status))!
            .SetValue(context, "closed");

        Assert.NotNull(context.NextTimeWindowAtUtc);
        Assert.Empty(context.TakeDueTimeWindows(ReleasedAtUtc.AddDays(1), maxWindows: 24));
    }

    [Fact]
    public void Completion_before_release_closes_the_context_after_release_arrives()
    {
        var operation = PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-001", "OP-001");
        var completedAtUtc = ReleasedAtUtc.AddHours(3);

        Assert.True(operation.Complete("SKU-FG-1000", 10, "WC-001", "EA", completedAtUtc));
        operation.ApplyRelease(
            "SKU-FG-1000",
            operationSequence: 10,
            "WC-001",
            ReleasedAtUtc,
            [PeriodicInspectionPlanSnapshot.From(NewPeriodicPlan())]);

        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal("closed", context.Status);
        Assert.Equal(completedAtUtc, context.CompletedAtUtc);
    }

    [Fact]
    public void Identical_duplicate_is_a_noop_but_conflicting_report_identity_is_rejected()
    {
        var operation = ReleasedOperation();
        var reportedAtUtc = ReleasedAtUtc.AddMinutes(10);

        Assert.True(operation.RecordProductionReport("RPT-001", "WC-001", 25m, "EA", reportedAtUtc, false, null));
        Assert.False(operation.RecordProductionReport("RPT-001", "WC-001", 25m, "EA", reportedAtUtc, false, null));
        Assert.Throws<InvalidOperationException>(() =>
            operation.RecordProductionReport("RPT-001", "WC-001", 30m, "EA", reportedAtUtc, false, null));
    }

    [Fact]
    public void Identical_release_replay_does_not_match_plans_that_appeared_after_the_original_release()
    {
        var operation = ReleasedOperation();

        operation.ApplyRelease(
            "SKU-FG-1000",
            operationSequence: 10,
            "WC-001",
            ReleasedAtUtc,
            [PeriodicInspectionPlanSnapshot.From(NewPeriodicPlan())]);

        Assert.Single(operation.RuntimeContexts);
    }

    [Fact]
    public void Conflicting_release_replay_is_rejected()
    {
        var operation = ReleasedOperation();

        Assert.Throws<InvalidOperationException>(() => operation.ApplyRelease(
            "SKU-FG-OTHER",
            operationSequence: 10,
            "WC-001",
            ReleasedAtUtc,
            [PeriodicInspectionPlanSnapshot.From(NewPeriodicPlan())]));
    }

    [Fact]
    public void Identical_completion_replay_is_a_noop_but_conflicting_completion_is_rejected()
    {
        var operation = ReleasedOperation();
        var completedAtUtc = ReleasedAtUtc.AddHours(3);

        Assert.True(operation.Complete("SKU-FG-1000", 10, "WC-001", "EA", completedAtUtc));
        Assert.False(operation.Complete("SKU-FG-1000", 10, "WC-001", "EA", completedAtUtc));
        Assert.Throws<InvalidOperationException>(() =>
            operation.Complete("SKU-FG-1000", 10, "WC-001", "EA", completedAtUtc.AddMinutes(1)));
    }

    [Theory]
    [InlineData(false, -1, null)]
    [InlineData(true, 1, "RPT-ORIGINAL")]
    public void Production_report_quantity_sign_must_match_reversal_semantics(
        bool isReversal,
        decimal goodQuantity,
        string? reversedReportNo)
    {
        var operation = ReleasedOperation();

        Assert.Throws<ArgumentOutOfRangeException>(() => operation.RecordProductionReport(
            "RPT-INVALID", "WC-001", goodQuantity, "EA", ReleasedAtUtc.AddMinutes(10), isReversal, reversedReportNo));
    }

    [Theory]
    [InlineData(true, -1, null)]
    [InlineData(false, 1, "RPT-ORIGINAL")]
    public void Reversal_lineage_must_be_present_only_for_reversal_reports(
        bool isReversal,
        decimal goodQuantity,
        string? reversedReportNo)
    {
        var operation = ReleasedOperation();

        Assert.Throws<ArgumentException>(() => operation.RecordProductionReport(
            "RPT-INVALID", "WC-001", goodQuantity, "EA", ReleasedAtUtc.AddMinutes(10), isReversal, reversedReportNo));
    }

    [Fact]
    public void Release_rejects_conflicting_staged_mes_operation_facts()
    {
        var operation = PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-001", "OP-001");
        operation.RecordProductionReport("RPT-001", "WC-OTHER", 25m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);

        Assert.Throws<InvalidOperationException>(() => operation.ApplyRelease(
            "SKU-FG-1000",
            operationSequence: 10,
            "WC-001",
            ReleasedAtUtc,
            [PeriodicInspectionPlanSnapshot.From(NewPeriodicPlan())]));
    }

    private static PeriodicInspectionOperation ReleasedOperation()
    {
        var operation = PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-001", "OP-001");
        operation.ApplyRelease(
            "SKU-FG-1000",
            operationSequence: 10,
            "WC-001",
            ReleasedAtUtc,
            [PeriodicInspectionPlanSnapshot.From(NewPeriodicPlan())]);
        return operation;
    }

    private static InspectionPlan NewPeriodicPlan()
    {
        var plan = InspectionPlan.Create(
            "org-001",
            "env-dev",
            "IQP-OPERATION-001",
            "operation",
            "SKU-FG-1000",
            null,
            "WC-001",
            null,
            "mes-operation",
            timeIntervalHours: 2m,
            quantityInterval: 100m,
            assignedTeamId: "team-quality-001");
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        return plan;
    }
}
