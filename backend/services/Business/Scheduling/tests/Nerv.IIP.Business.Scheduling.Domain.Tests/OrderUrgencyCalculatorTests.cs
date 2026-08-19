using Nerv.IIP.Business.Scheduling.Domain.Services;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

public sealed class OrderUrgencyCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Calculates_cr_and_slack_without_hiding_business_or_execution_contributions()
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddHours(8),
            remainingCycle: TimeSpan.FromHours(10),
            priority: BusinessPriorityLevel.P1,
            risks: [new ExecutionRiskFact("equipment.unavailable", ExecutionRiskCategory.Equipment, true, "DEV-CNC-03", Now)]));

        Assert.Equal(-2m, result.SlackHours);
        Assert.Equal(0.8m, result.CriticalRatio);
        Assert.Equal(2m, result.ExpectedDelayHours);
        Assert.Equal(Now.AddHours(10), result.TimeCriticality.EstimatedCompletionUtc);
        Assert.Equal(OrderUrgencyLevel.Urgent, result.Level);
        Assert.Equal(BusinessPriorityLevel.P1, result.BusinessPriority.Level);
        Assert.Equal(OrderUrgencyLevel.Urgent, result.TimeCriticality.Level);
        Assert.Equal(OrderUrgencyLevel.HighRisk, result.ExecutionRisk.Level);
        Assert.Contains("business.priority.p1", result.BusinessPriority.ReasonCodes);
        Assert.Equal(
            ["time.cr.belowOne", "time.slack.negative"],
            result.TimeCriticality.ReasonCodes);
        Assert.Contains("equipment.unavailable", result.ExecutionRisk.ReasonCodes);
    }

    [Theory]
    [InlineData(BusinessPriorityLevel.P0, OrderUrgencyLevel.Critical)]
    [InlineData(BusinessPriorityLevel.P1, OrderUrgencyLevel.Urgent)]
    [InlineData(BusinessPriorityLevel.P2, OrderUrgencyLevel.Normal)]
    [InlineData(BusinessPriorityLevel.P3, OrderUrgencyLevel.Normal)]
    public void Preserves_explainable_business_priority_levels(
        BusinessPriorityLevel priority,
        OrderUrgencyLevel expected)
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            priority: priority));

        Assert.Equal(expected, result.Level);
        Assert.Equal(priority, result.BusinessPriority.Level);
        Assert.Contains($"business.priority.{priority.ToString().ToLowerInvariant()}", result.BusinessPriority.ReasonCodes);
    }

    [Theory]
    [InlineData("material.shortage", ExecutionRiskCategory.Material)]
    [InlineData("equipment.unavailable", ExecutionRiskCategory.Equipment)]
    [InlineData("quality.hold", ExecutionRiskCategory.Quality)]
    [InlineData("tooling.unavailable", ExecutionRiskCategory.Tooling)]
    [InlineData("capacity.insufficient", ExecutionRiskCategory.Capacity)]
    public void Blocking_execution_facts_are_high_risk_and_keep_their_reason(
        string reasonCode,
        ExecutionRiskCategory category)
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            risks: [new ExecutionRiskFact(reasonCode, category, true, "source-1", Now)]));

        Assert.Equal(OrderUrgencyLevel.HighRisk, result.Level);
        Assert.Equal(OrderUrgencyLevel.HighRisk, result.ExecutionRisk.Level);
        Assert.Contains(reasonCode, result.ExecutionRisk.ReasonCodes);
    }

    [Theory]
    [InlineData(true, false, "urgency.source.missing")]
    [InlineData(false, true, "urgency.source.stale")]
    public void Missing_or_stale_sources_fail_closed(bool missing, bool stale, string reasonCode)
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            sourceMissing: missing,
            sourceStale: stale));

        Assert.Equal(OrderUrgencyLevel.HighRisk, result.Level);
        Assert.Contains(reasonCode, result.ExecutionRisk.ReasonCodes);
    }

    [Fact]
    public void Time_progression_is_deterministic_and_can_upgrade_an_order()
    {
        var first = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddHours(20),
            remainingCycle: TimeSpan.FromHours(8)));
        var later = OrderUrgencyCalculator.Calculate(Input(
            calculatedAtUtc: Now.AddHours(13),
            dueUtc: Now.AddHours(20),
            remainingCycle: TimeSpan.FromHours(8)));
        var repeated = OrderUrgencyCalculator.Calculate(Input(
            calculatedAtUtc: Now.AddHours(13),
            dueUtc: Now.AddHours(20),
            remainingCycle: TimeSpan.FromHours(8)));

        Assert.Equal(OrderUrgencyLevel.Normal, first.Level);
        Assert.Equal(OrderUrgencyLevel.Urgent, later.Level);
        Assert.Equal(later.Level, repeated.Level);
        Assert.Equal(later.CriticalRatio, repeated.CriticalRatio);
        Assert.Equal(later.SlackHours, repeated.SlackHours);
        Assert.Equal(later.TimeCriticality.ReasonCodes, repeated.TimeCriticality.ReasonCodes);
        Assert.Equal(later.ExecutionRisk.ReasonCodes, repeated.ExecutionRisk.ReasonCodes);
        Assert.Equal(-1m, later.SlackHours);
        Assert.Equal(0.875m, later.CriticalRatio);
    }

    [Fact]
    public void Slack_within_one_shift_is_high_risk_even_when_cr_is_not_close_to_one()
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddHours(15),
            remainingCycle: TimeSpan.FromHours(8)));

        Assert.Equal(7m, result.SlackHours);
        Assert.Equal(1.875m, result.CriticalRatio);
        Assert.Equal(OrderUrgencyLevel.HighRisk, result.TimeCriticality.Level);
        Assert.Contains("time.slack.withinShift", result.TimeCriticality.ReasonCodes);
    }

    [Fact]
    public void Reason_codes_are_distinct_and_stably_sorted()
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            risks:
            [
                new ExecutionRiskFact("quality.hold", ExecutionRiskCategory.Quality, true, "Q-1", Now),
                new ExecutionRiskFact("material.shortage", ExecutionRiskCategory.Material, true, "M-1", Now),
                new ExecutionRiskFact("quality.hold", ExecutionRiskCategory.Quality, true, "Q-1", Now),
            ]));

        Assert.Equal(["material.shortage", "quality.hold"], result.ExecutionRisk.ReasonCodes);
    }

    [Fact]
    public void Zero_remaining_cycle_is_valid_and_keeps_time_outputs_exact()
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddHours(8),
            remainingCycle: TimeSpan.Zero));

        Assert.Null(result.CriticalRatio);
        Assert.Equal(8m, result.SlackHours);
        Assert.Equal(0m, result.ExpectedDelayHours);
        Assert.Equal(Now, result.TimeCriticality.EstimatedCompletionUtc);
        Assert.Equal(0m, result.TimeCriticality.RemainingCycleHours);
    }

    [Theory]
    [InlineData(null, "WO-001")]
    [InlineData("", "WO-001")]
    [InlineData("   ", "WO-001")]
    [InlineData(" SO-777 ", "SO-777")]
    public void Business_reference_falls_back_to_order_id_or_is_trimmed(
        string? businessReference,
        string expected)
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            businessReference: businessReference));

        Assert.Equal(expected, result.BusinessReference);
    }

    [Theory]
    [InlineData(-1, OrderUrgencyLevel.Normal, true)]
    [InlineData(0, OrderUrgencyLevel.Normal, true)]
    [InlineData(1, OrderUrgencyLevel.Critical, false)]
    public void P0_priority_expires_at_the_inclusive_boundary(
        int expiresAfterHours,
        OrderUrgencyLevel expected,
        bool expectedExpiredReason)
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            priority: BusinessPriorityLevel.P0,
            priorityExpiresAtUtc: Now.AddHours(expiresAfterHours)));

        Assert.Equal(expected, result.BusinessPriority.UrgencyLevel);
        Assert.Equal(
            expectedExpiredReason,
            result.BusinessPriority.ReasonCodes.Contains("business.priority.expired"));
    }

    [Theory]
    [InlineData(0, 0, OrderUrgencyLevel.HighRisk, "time.slack.withinShift")]
    [InlineData(8, 8, OrderUrgencyLevel.HighRisk, "time.slack.withinShift")]
    [InlineData(16, 8, OrderUrgencyLevel.Normal, "time.withinCommitment")]
    [InlineData(48, 40, OrderUrgencyLevel.Attention, "time.cr.attention")]
    public void Time_thresholds_preserve_their_exact_domain_boundaries(
        int dueAfterHours,
        int remainingHours,
        OrderUrgencyLevel expected,
        string expectedReason)
    {
        var result = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddHours(dueAfterHours),
            remainingCycle: TimeSpan.FromHours(remainingHours)));

        Assert.Equal(expected, result.TimeCriticality.Level);
        Assert.Equal([expectedReason], result.TimeCriticality.ReasonCodes);
    }

    [Fact]
    public void Non_blocking_risk_is_attention_while_no_risk_is_normal()
    {
        var noRisk = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8)));
        var nonBlockingRisk = OrderUrgencyCalculator.Calculate(Input(
            dueUtc: Now.AddDays(2),
            remainingCycle: TimeSpan.FromHours(8),
            risks:
            [
                new ExecutionRiskFact(
                    "material.watch",
                    ExecutionRiskCategory.Material,
                    false,
                    "MAT-001",
                    Now),
            ]));

        Assert.Equal(OrderUrgencyLevel.Normal, noRisk.ExecutionRisk.Level);
        Assert.Equal(["execution.risk.none"], noRisk.ExecutionRisk.ReasonCodes);
        Assert.Equal(OrderUrgencyLevel.Attention, nonBlockingRisk.ExecutionRisk.Level);
        Assert.Equal(["material.watch"], nonBlockingRisk.ExecutionRisk.ReasonCodes);
    }

    private static OrderUrgencyCalculationInput Input(
        DateTimeOffset? dueUtc,
        TimeSpan remainingCycle,
        DateTimeOffset? calculatedAtUtc = null,
        BusinessPriorityLevel priority = BusinessPriorityLevel.P2,
        IReadOnlyCollection<ExecutionRiskFact>? risks = null,
        bool sourceMissing = false,
        bool sourceStale = false,
        string? businessReference = "SO-001",
        DateTimeOffset? priorityExpiresAtUtc = null)
    {
        return new OrderUrgencyCalculationInput(
            "WO-001",
            businessReference!,
            calculatedAtUtc ?? Now,
            dueUtc,
            remainingCycle,
            new BusinessPriorityFact(priority, "planner", "capacity commitment", Now, priorityExpiresAtUtc, 1),
            risks ?? [],
            sourceMissing,
            sourceStale,
            Now,
            "input-fingerprint");
    }
}
