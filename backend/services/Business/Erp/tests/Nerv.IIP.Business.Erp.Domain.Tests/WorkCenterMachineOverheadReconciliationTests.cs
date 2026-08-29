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
        Assert.True(zero.IsReadyForClose);
        Assert.True(expensedDowntime.IsReadyForClose);
        Assert.Throws<ArgumentException>(() => Record(
            1_000m, 200m, 0, 0m, 0m, 0m,
            abnormalDowntimeHours: 10,
            disposition: AbnormalDowntimeDisposition.None));
    }

    [Fact]
    public void Persistence_precision_is_normalized_before_derived_values_are_frozen()
    {
        var reconciliation = WorkCenterMachineOverheadReconciliation.Record(
            "org-a", "env-a", "WC-01", "2026-08",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()), 1, "CNY",
            1.0000005m, 2.0000005m, 1,
            0.1000005m, 0.2000005m, 0.3000015m,
            0, AbnormalDowntimeDisposition.None, 1,
            "user:accountant", "ledger:2026-08", "fractional precision",
            new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero));

        Assert.Equal(1.000001m, reconciliation.ActualFixedOverheadAmount);
        Assert.Equal(2.000001m, reconciliation.ActualVariableOverheadAmount);
        Assert.Equal(3.000002m, reconciliation.ActualTotalOverheadAmount);
        Assert.Equal(0.100001m, reconciliation.AppliedFixedAmount);
        Assert.Equal(0.200001m, reconciliation.AppliedVariableAmount);
        Assert.Equal(0.300002m, reconciliation.AppliedTotalAmount);
        Assert.Equal(0m, reconciliation.AppliedRoundingDifferenceAmount);
        Assert.Equal(0.000000000028m, reconciliation.AppliedMachineHours);
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
