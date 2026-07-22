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
        Assert.Equal(OrderUrgencyLevel.Urgent, result.Level);
        Assert.Equal(BusinessPriorityLevel.P1, result.BusinessPriority.Level);
        Assert.Equal(OrderUrgencyLevel.Urgent, result.TimeCriticality.Level);
        Assert.Equal(OrderUrgencyLevel.HighRisk, result.ExecutionRisk.Level);
        Assert.Contains("business.priority.p1", result.BusinessPriority.ReasonCodes);
        Assert.Contains("time.cr.belowOne", result.TimeCriticality.ReasonCodes);
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

    private static OrderUrgencyCalculationInput Input(
        DateTimeOffset? dueUtc,
        TimeSpan remainingCycle,
        DateTimeOffset? calculatedAtUtc = null,
        BusinessPriorityLevel priority = BusinessPriorityLevel.P2,
        IReadOnlyCollection<ExecutionRiskFact>? risks = null,
        bool sourceMissing = false,
        bool sourceStale = false)
    {
        return new OrderUrgencyCalculationInput(
            "WO-001",
            "SO-001",
            calculatedAtUtc ?? Now,
            dueUtc,
            remainingCycle,
            new BusinessPriorityFact(priority, "planner", "capacity commitment", Now, null, 1),
            risks ?? [],
            sourceMissing,
            sourceStale,
            Now,
            "input-fingerprint");
    }
}
