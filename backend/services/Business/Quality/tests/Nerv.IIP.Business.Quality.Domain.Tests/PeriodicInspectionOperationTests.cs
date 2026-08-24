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
    public void Later_arriving_report_with_later_business_time_does_not_replace_first_activity()
    {
        var operation = ReleasedOperation();

        operation.RecordProductionReport("RPT-001", "WC-001", 10m, "EA", ReleasedAtUtc.AddMinutes(10), false, null);
        operation.RecordProductionReport("RPT-002", "WC-001", 20m, "EA", ReleasedAtUtc.AddMinutes(40), false, null);

        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(ReleasedAtUtc.AddMinutes(10), context.FirstActivityAtUtc);
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
