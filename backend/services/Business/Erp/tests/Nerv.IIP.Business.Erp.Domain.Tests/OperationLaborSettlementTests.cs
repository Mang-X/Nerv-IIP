using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class OperationLaborSettlementTests
{
    private static readonly DateTimeOffset CompletedAtUtc =
        new(2026, 8, 31, 15, 50, 0, TimeSpan.Zero);

    [Fact]
    public void Create_freezes_actual_hours_at_standard_rate()
    {
        var rateId = new WorkCenterCostRateId(Guid.CreateVersion7());

        var settlement = OperationLaborSettlement.Create(
            "org-001",
            "env-prod",
            "WO-001",
            "OP-001",
            "WC-01",
            7,
            CompletedAtUtc,
            2 * TimeSpan.TicksPerHour,
            rateId,
            7,
            "CNY",
            80m,
            "evt-settled-001",
            "payload-sha256");

        Assert.Equal(2m, settlement.ActualLaborHours);
        Assert.Equal(160m, settlement.Amount);
        Assert.Equal("standard", settlement.RateBasis);
        Assert.Equal(CompletedAtUtc, settlement.RateBasisAtUtc);
        Assert.Equal(rateId, settlement.WorkCenterCostRateId);
        Assert.Equal(7, settlement.RateRevision);
        Assert.Equal("CNY", settlement.CurrencyCode);
    }

    [Fact]
    public void Void_copies_the_frozen_snapshot_and_is_an_exact_reverse()
    {
        var settlement = OperationLaborSettlement.Create(
            "org-001",
            "env-prod",
            "WO-001",
            "OP-001",
            "WC-01",
            1,
            CompletedAtUtc,
            90 * TimeSpan.TicksPerMinute,
            new WorkCenterCostRateId(Guid.CreateVersion7()),
            8,
            "CNY",
            88m,
            "evt-settled-001",
            "settled-payload-sha256");

        var reversal = OperationLaborSettlementVoid.Create(
            settlement,
            CompletedAtUtc.AddDays(1),
            "evt-void-001",
            "void-payload-sha256");

        Assert.Equal(-132m, reversal.Amount);
        Assert.Equal(settlement.ActualLaborTicks, reversal.ActualLaborTicks);
        Assert.Equal(settlement.HourlyRate, reversal.HourlyRate);
        Assert.Equal(settlement.WorkCenterCostRateId, reversal.WorkCenterCostRateId);
        Assert.Equal(settlement.RateRevision, reversal.RateRevision);
        Assert.Equal(settlement.CurrencyCode, reversal.CurrencyCode);
        Assert.Equal(settlement.RateBasisAtUtc, reversal.RateBasisAtUtc);
    }

    [Fact]
    public void State_never_reactivates_a_voided_or_older_revision()
    {
        var state = OperationLaborSettlementState.Open("org-001", "env-prod", "OP-001");

        Assert.Equal(OperationLaborSettlementTransition.Activated, state.ApplySettlement(1).Transition);
        Assert.Equal(OperationLaborSettlementTransition.Voided, state.ApplyVoid(1).Transition);
        Assert.Equal(OperationLaborSettlementTransition.IgnoredVoided, state.ApplySettlement(1).Transition);
        Assert.Equal(OperationLaborSettlementTransition.Activated, state.ApplySettlement(2).Transition);

        var oldSettlement = state.ApplySettlement(1);

        Assert.Equal(OperationLaborSettlementTransition.IgnoredOldRevision, oldSettlement.Transition);
        Assert.Equal(2, state.HighestRevision);
        Assert.Equal(2, state.ActiveRevision);
    }
}
