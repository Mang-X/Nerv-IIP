using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class OperationMachineOverheadSettlementTests
{
    [Fact]
    public void Applied_snapshot_prices_lossless_ticks_and_rounds_each_amount_to_even()
    {
        var settlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-001", "env-prod", "WO-001", "OP-001", "WC-01", 3,
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"),
            "DEVICE-001", 18_000_000_000,
            "single-device-active-minus-explicit-pause-v1",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()),
            "2026-08", 7, "CNY", 0.000001m, 0.000003m,
            "evt-settled-r3", "payload-hash");

        Assert.Equal(0.5m, settlement.ActualMachineHours);
        Assert.Equal(0m, settlement.FixedAmount);
        Assert.Equal(0.000002m, settlement.VariableAmount);
        Assert.Equal(0.000002m, settlement.Amount);
    }

    [Fact]
    public void Applied_snapshot_does_not_round_hours_before_multiplication()
    {
        var settlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-001", "env-prod", "WO-001", "OP-001", "WC-01", 1,
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"),
            "DEVICE-001", 1,
            "single-device-active-minus-explicit-pause-v1",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()),
            "2026-08", 1, "CNY", 54_000m, 0m,
            "evt-settled-r1", "payload-hash");

        Assert.Equal(0.000002m, settlement.FixedAmount);
        Assert.Equal(0.000002m, settlement.Amount);
    }

    [Fact]
    public void Void_copies_the_frozen_snapshot_and_negates_each_amount_exactly()
    {
        var settlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-001", "env-prod", "WO-001", "OP-001", "WC-01", 1,
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"),
            "DEVICE-001", 27_000_000_000,
            "single-device-active-minus-explicit-pause-v1",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()),
            "2026-08", 4, "CNY", 12.345679m, 3.456789m,
            "evt-settled-r1", "payload-hash");

        var reversal = OperationMachineOverheadSettlementVoid.Create(
            settlement,
            DateTimeOffset.Parse("2026-09-02T00:00:00Z"),
            "evt-void-r1",
            "void-payload-hash");

        Assert.Equal(-settlement.FixedAmount, reversal.FixedAmount);
        Assert.Equal(-settlement.VariableAmount, reversal.VariableAmount);
        Assert.Equal(-settlement.Amount, reversal.Amount);
        Assert.Equal(settlement.WorkCenterMachineOverheadRateId, reversal.WorkCenterMachineOverheadRateId);
        Assert.Equal(settlement.ActualMachineTicks, reversal.ActualMachineTicks);
    }

    [Fact]
    public void Explicit_not_applicable_snapshot_carries_rate_identity_but_no_machine_evidence()
    {
        var settlement = OperationMachineOverheadSettlement.CreateNotApplicable(
            "org-001", "env-prod", "WO-001", "OP-001", "WC-01", 1,
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"),
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()),
            "2026-08", 2, "CNY", "evt-settled-r1", "payload-hash");

        Assert.Equal(MachineOverheadApplicability.NotApplicable, settlement.Applicability);
        Assert.Null(settlement.ActualMachineTicks);
        Assert.Null(settlement.DeviceAssetId);
        Assert.Equal(0m, settlement.Amount);
    }

    [Fact]
    public void Lifecycle_watermark_rejects_old_or_voided_revisions_and_allows_reopen()
    {
        var state = OperationMachineOverheadSettlementState.Open("org-001", "env-prod", "OP-001");

        Assert.Equal(OperationMachineOverheadSettlementTransition.Activated, state.ApplySettlement(1).Transition);
        Assert.Equal(OperationMachineOverheadSettlementTransition.Voided, state.ApplyVoid(1).Transition);
        Assert.Equal(OperationMachineOverheadSettlementTransition.IgnoredVoided, state.ApplySettlement(1).Transition);
        Assert.Equal(OperationMachineOverheadSettlementTransition.Activated, state.ApplySettlement(2).Transition);
        Assert.Equal(OperationMachineOverheadSettlementTransition.IgnoredOldRevision, state.ApplyVoid(1).Transition);
    }

    [Fact]
    public void Work_order_records_machine_overhead_as_an_independent_cost_element()
    {
        var cost = WorkOrderCost.Open("org-001", "env-prod", "WO-001", "SKU-001");
        cost.RecordLabor("RPT-001", "WC-01", 1m, 80m, "CNY", false,
            DateTimeOffset.Parse("2026-08-31T10:00:00Z"));
        var settlement = AppliedSettlement("CNY", 2 * TimeSpan.TicksPerHour, 30m, 10m);

        Assert.True(cost.TryFreezeMachineOverheadCurrency("CNY"));
        cost.RecordMachineOverhead(settlement);

        Assert.Equal(80m, cost.LaborCost);
        Assert.Equal(80m, cost.MachineOverheadCost);
        Assert.Equal(160m, cost.TotalAccumulatedCost);
        Assert.Single(cost.Details, detail => detail.Type == WorkOrderCostDetailType.MachineOverhead);
    }

    [Fact]
    public void Work_order_rejects_currency_conflicts_regardless_of_arrival_order()
    {
        var laborFirst = WorkOrderCost.Open("org-001", "env-prod", "WO-001", "SKU-001");
        laborFirst.RecordLabor("RPT-001", "WC-01", 1m, 80m, "CNY", false,
            DateTimeOffset.Parse("2026-08-31T10:00:00Z"));
        Assert.False(laborFirst.TryFreezeMachineOverheadCurrency("USD"));

        var machineFirst = WorkOrderCost.Open("org-001", "env-prod", "WO-002", "SKU-001");
        Assert.True(machineFirst.TryFreezeMachineOverheadCurrency("CNY"));
        machineFirst.RecordMachineOverhead(AppliedSettlement("CNY", TimeSpan.TicksPerHour, 30m, 10m));
        Assert.False(machineFirst.TryFreezeLaborCurrency("USD"));
    }

    private static OperationMachineOverheadSettlement AppliedSettlement(
        string currencyCode,
        long ticks,
        decimal fixedRate,
        decimal variableRate)
        => OperationMachineOverheadSettlement.CreateApplied(
            "org-001", "env-prod", "WO-001", "OP-001", "WC-01", 1,
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"),
            "DEVICE-001", ticks,
            "single-device-active-minus-explicit-pause-v1",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()),
            "2026-08", 1, currencyCode, fixedRate, variableRate,
            "evt-settled-r1", "payload-hash");
}
