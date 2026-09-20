using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class WorkCenterMachineOverheadReconciliationTests
{
    [Fact]
    public void Low_load_preserves_predetermined_allocation_and_lists_unallocated_fixed_overhead()
    {
        var reconciliation = Record(
            actualFixed: 30_000m,
            actualVariable: 8_000m,
            appliedHours: 600,
            appliedFixed: 18_000m,
            appliedVariable: 6_000m,
            appliedTotal: 24_000m);

        Assert.Equal(600m, reconciliation.AppliedMachineHours);
        Assert.Equal(18_000m, reconciliation.AppliedFixedAmount);
        Assert.Equal(12_000m, reconciliation.UnderOverAppliedFixedAmount);
        Assert.Equal(2_000m, reconciliation.UnderOverAppliedVariableAmount);
        Assert.Equal(14_000m, reconciliation.UnderOverAppliedTotalAmount);
        Assert.Equal(12_000m, reconciliation.UnallocatedFixedOverheadAmount);
        Assert.Equal(0m, reconciliation.OverAppliedFixedOverheadAmount);
        Assert.Equal(
            reconciliation.AppliedTotalAmount,
            reconciliation.AppliedFixedAmount + reconciliation.AppliedVariableAmount);
        Assert.Equal(
            reconciliation.UnderOverAppliedFixedAmount,
            reconciliation.UnallocatedFixedOverheadAmount - reconciliation.OverAppliedFixedOverheadAmount);
    }

    [Fact]
    public void High_load_lists_reverse_variance_without_rewriting_applied_amounts()
    {
        var reconciliation = Record(
            actualFixed: 30_000m,
            actualVariable: 10_000m,
            appliedHours: 1_200,
            appliedFixed: 36_000m,
            appliedVariable: 12_000m,
            appliedTotal: 48_000m);

        Assert.Equal(36_000m, reconciliation.AppliedFixedAmount);
        Assert.Equal(-6_000m, reconciliation.UnderOverAppliedFixedAmount);
        Assert.Equal(-2_000m, reconciliation.UnderOverAppliedVariableAmount);
        Assert.Equal(-8_000m, reconciliation.UnderOverAppliedTotalAmount);
        Assert.Equal(0m, reconciliation.UnallocatedFixedOverheadAmount);
        Assert.Equal(6_000m, reconciliation.OverAppliedFixedOverheadAmount);
        Assert.Equal(
            reconciliation.UnderOverAppliedFixedAmount,
            reconciliation.UnallocatedFixedOverheadAmount - reconciliation.OverAppliedFixedOverheadAmount);
    }

    [Fact]
    public void Abnormal_downtime_is_separate_from_product_hours_and_pending_blocks_close()
    {
        var reconciliation = Record(
            actualFixed: 30_000m,
            actualVariable: 10_000m,
            appliedHours: 700,
            appliedFixed: 21_000m,
            appliedVariable: 7_000m,
            appliedTotal: 28_000m,
            abnormalDowntimeHours: 50,
            disposition: AbnormalDowntimeDisposition.Pending);

        Assert.Equal(700m, reconciliation.AppliedMachineHours);
        Assert.Equal(50m, reconciliation.AbnormalDowntimeHours);
        Assert.False(reconciliation.IsReadyForClose);
    }

    [Fact]
    public void Zero_hours_and_period_expense_disposition_are_explicit_vectors()
    {
        var zero = Record(1_000m, 200m, 0, 0m, 0m, 0m);
        var expensedDowntime = Record(
            1_000m, 200m, 0, 0m, 0m, 0m,
            abnormalDowntimeHours: 10,
            disposition: AbnormalDowntimeDisposition.PeriodExpense);

        Assert.Equal(1_200m, zero.UnderOverAppliedTotalAmount);
        Assert.Equal(0m, zero.AppliedFixedAmount);
        Assert.Equal(0m, zero.AppliedVariableAmount);
        Assert.Equal(0m, zero.AppliedTotalAmount);
        Assert.Equal(0m, zero.AppliedRoundingDifferenceAmount);
        Assert.True(zero.IsReadyForClose);
        Assert.True(expensedDowntime.IsReadyForClose);
        Assert.Throws<ArgumentException>(() => Record(
            1_000m, 200m, 0, 0m, 0m, 0m,
            abnormalDowntimeHours: 10,
            disposition: AbnormalDowntimeDisposition.None));
    }

    [Fact]
    public void Amount_chain_uses_six_decimal_to_even_for_positive_and_negative_midpoints()
    {
        var positive = WorkCenterMachineOverheadReconciliation.Record(
            "org-a", "env-a", "WC-01", "2026-08",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()), 1, "CNY",
            1.0000005m, 1.0000015m, 1,
            0m, 0m, 0m,
            0, AbnormalDowntimeDisposition.None, 1,
            "user:accountant", "ledger:2026-08-positive", "positive midpoint precision",
            new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero));
        var negative = WorkCenterMachineOverheadReconciliation.Record(
            "org-a", "env-a", "WC-01", "2026-08",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()), 1, "CNY",
            0m, 0m, 1,
            1.0000005m, 1.0000015m, 2.0000025m,
            0, AbnormalDowntimeDisposition.None, 2,
            "user:accountant", "ledger:2026-08-negative", "negative midpoint precision",
            new DateTimeOffset(2026, 8, 31, 16, 1, 0, TimeSpan.Zero));

        Assert.Equal(1.000000m, positive.ActualFixedOverheadAmount);
        Assert.Equal(1.000002m, positive.ActualVariableOverheadAmount);
        Assert.Equal(2.000002m, positive.ActualTotalOverheadAmount);
        Assert.Equal(1.000000m, positive.UnderOverAppliedFixedAmount);
        Assert.Equal(1.000002m, positive.UnderOverAppliedVariableAmount);
        Assert.Equal(2.000002m, positive.UnderOverAppliedTotalAmount);
        Assert.Equal(1.000000m, positive.UnallocatedFixedOverheadAmount);
        Assert.Equal(0m, positive.OverAppliedFixedOverheadAmount);

        Assert.Equal(1.000000m, negative.AppliedFixedAmount);
        Assert.Equal(1.000002m, negative.AppliedVariableAmount);
        Assert.Equal(2.000002m, negative.AppliedTotalAmount);
        Assert.Equal(0m, negative.AppliedRoundingDifferenceAmount);
        Assert.Equal(-1.000000m, negative.UnderOverAppliedFixedAmount);
        Assert.Equal(-1.000002m, negative.UnderOverAppliedVariableAmount);
        Assert.Equal(-2.000002m, negative.UnderOverAppliedTotalAmount);
        Assert.Equal(0m, negative.UnallocatedFixedOverheadAmount);
        Assert.Equal(1.000000m, negative.OverAppliedFixedOverheadAmount);
        Assert.Equal(negative.AppliedTotalAmount,
            negative.AppliedFixedAmount + negative.AppliedVariableAmount + negative.AppliedRoundingDifferenceAmount);
        Assert.Equal(0.000000000028m, positive.AppliedMachineHours);
        Assert.Equal(0.000000000028m, negative.AppliedMachineHours);
    }

    private static WorkCenterMachineOverheadReconciliation Record(
        decimal actualFixed,
        decimal actualVariable,
        long appliedHours,
        decimal appliedFixed,
        decimal appliedVariable,
        decimal appliedTotal,
        long abnormalDowntimeHours = 0,
        AbnormalDowntimeDisposition disposition = AbnormalDowntimeDisposition.None)
        => WorkCenterMachineOverheadReconciliation.Record(
            "org-a", "env-a", "WC-01", "2026-08",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()), 1, "CNY",
            actualFixed, actualVariable, appliedHours * TimeSpan.TicksPerHour,
            appliedFixed, appliedVariable, appliedTotal,
            abnormalDowntimeHours * TimeSpan.TicksPerHour, disposition,
            1, "user:accountant", "ledger:2026-08", "month-end reconciliation",
            new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero));
}
