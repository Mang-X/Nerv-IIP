using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class WorkCenterMachineOverheadRateTests
{
    private static readonly DateTimeOffset ChangedAtUtc =
        new(2026, 6, 25, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Applicable_rate_derives_fixed_variable_and_total_rates_from_normal_capacity()
    {
        // DomainInvariant: #2280 normal-month vector. Replacing normal capacity with
        // the 600-hour low-load actual volume would incorrectly produce 50/16.666667.
        var rate = WorkCenterMachineOverheadRate.DefineApplicable(
            " org-001 ",
            " env-prod ",
            " WC-MACHINING ",
            " 2026-06 ",
            fixedOverheadBudget: 30_000m,
            variableOverheadBudget: 10_000m,
            normalCapacityMachineHours: 1_000m,
            currencyCode: " cny ",
            revision: 2,
            changedBy: "user:finance-admin",
            reason: " 月度预定分配率 ",
            changedAtUtc: ChangedAtUtc);

        Assert.Equal("org-001", rate.OrganizationId);
        Assert.Equal("env-prod", rate.EnvironmentId);
        Assert.Equal("WC-MACHINING", rate.WorkCenterId);
        Assert.Equal("2026-06", rate.AccountingPeriodCode);
        Assert.Equal(MachineOverheadApplicability.Applicable, rate.Applicability);
        Assert.Equal(30_000m, rate.FixedOverheadBudget);
        Assert.Equal(10_000m, rate.VariableOverheadBudget);
        Assert.Equal(1_000m, rate.NormalCapacityMachineHours);
        Assert.Equal(30m, rate.FixedHourlyRate);
        Assert.Equal(10m, rate.VariableHourlyRate);
        Assert.Equal(40m, rate.TotalHourlyRate);
        Assert.Equal("CNY", rate.CurrencyCode);
        Assert.Equal(2, rate.Revision);
        Assert.Equal("user:finance-admin", rate.ChangedBy);
        Assert.Equal("月度预定分配率", rate.Reason);
        Assert.Equal(ChangedAtUtc, rate.ChangedAtUtc);
    }

    [Fact]
    public void Applicable_rate_uses_bankers_rounding_for_exact_six_decimal_midpoints()
    {
        // DomainInvariant: the approved six-decimal rule is midpoint-to-even.
        // 1 / 128 ends in ...8125 and must round down because the retained digit is even;
        // 3 / 128 ends in ...4375 and must round up because the retained digit is odd.
        var rate = WorkCenterMachineOverheadRate.DefineApplicable(
            "org", "env", "WC", "2026-06",
            fixedOverheadBudget: 1m,
            variableOverheadBudget: 3m,
            normalCapacityMachineHours: 128m,
            currencyCode: "CNY",
            revision: 1,
            changedBy: "user:finance",
            reason: "bankers rounding vector",
            changedAtUtc: ChangedAtUtc);

        Assert.Equal(0.007812m, rate.FixedHourlyRate);
        Assert.Equal(0.023438m, rate.VariableHourlyRate);
        Assert.Equal(0.031250m, rate.TotalHourlyRate);
    }

    [Theory]
    [InlineData(-1, 10, 100)]
    [InlineData(10, -1, 100)]
    [InlineData(0, 0, 100)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 0, -1)]
    public void Applicable_rate_rejects_invalid_budget_or_normal_capacity(
        decimal fixedBudget,
        decimal variableBudget,
        decimal normalCapacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org", "env", "WC", "2026-06",
                fixedBudget, variableBudget, normalCapacity, "CNY", 1,
                "user:finance", "initial rate", ChangedAtUtc));
    }

    [Fact]
    public void Not_applicable_revision_has_explicit_status_and_zero_cost_values()
    {
        var rate = WorkCenterMachineOverheadRate.DefineNotApplicable(
            "org", "env", "WC-MANUAL", "2026-06", "CNY", 1,
            "user:finance", "纯手工作业中心", ChangedAtUtc);

        Assert.Equal(MachineOverheadApplicability.NotApplicable, rate.Applicability);
        Assert.Equal(0m, rate.FixedOverheadBudget);
        Assert.Equal(0m, rate.VariableOverheadBudget);
        Assert.Equal(0m, rate.NormalCapacityMachineHours);
        Assert.Equal(0m, rate.FixedHourlyRate);
        Assert.Equal(0m, rate.VariableHourlyRate);
        Assert.Equal(0m, rate.TotalHourlyRate);
    }

    [Fact]
    public void Audit_timestamp_must_be_a_nondefault_utc_instant()
    {
        Assert.Throws<ArgumentException>(() =>
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org", "env", "WC", "2026-06",
                10m, 0m, 100m, "CNY", 1,
                "user:finance", "initial rate", default));

        Assert.Throws<ArgumentException>(() =>
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org", "env", "WC", "2026-06",
                10m, 0m, 100m, "CNY", 1,
                "user:finance", "initial rate",
                new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(8))));
    }

}
