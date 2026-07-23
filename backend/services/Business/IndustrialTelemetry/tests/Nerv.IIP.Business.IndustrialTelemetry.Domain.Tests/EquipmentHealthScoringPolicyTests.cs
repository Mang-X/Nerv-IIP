using System.Collections.Immutable;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.EquipmentHealth;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests;

public sealed class EquipmentHealthScoringPolicyTests
{
    private static readonly DateTimeOffset AsOfUtc =
        new(2026, 7, 24, 4, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(EquipmentHealthRiskDirection.High, 80, EquipmentHealthRuleStatus.Risk)]
    [InlineData(EquipmentHealthRiskDirection.High, 79.999, EquipmentHealthRuleStatus.Normal)]
    [InlineData(EquipmentHealthRiskDirection.High, 101, EquipmentHealthRuleStatus.Risk)]
    [InlineData(EquipmentHealthRiskDirection.Low, 120, EquipmentHealthRuleStatus.Risk)]
    [InlineData(EquipmentHealthRiskDirection.Low, 120.001, EquipmentHealthRuleStatus.Normal)]
    [InlineData(EquipmentHealthRiskDirection.Low, 99, EquipmentHealthRuleStatus.Risk)]
    public void Threshold_proximity_uses_signed_safe_side_distance_and_includes_boundary(
        EquipmentHealthRiskDirection direction,
        double currentValue,
        EquipmentHealthRuleStatus expectedStatus)
    {
        var input = NormalInput() with
        {
            RuleObservations = [Rule(direction: direction, currentValue: currentValue)],
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);
        var evaluation = Evaluation(result, EquipmentHealthScoringPolicy.ThresholdProximityRuleCode);

        Assert.Equal(expectedStatus, evaluation.Status);
        Assert.Equal(expectedStatus == EquipmentHealthRuleStatus.Risk ? 15 : 0, evaluation.Penalty);
    }

    [Theory]
    [InlineData(19.999, EquipmentHealthRuleStatus.Normal, 0)]
    [InlineData(20, EquipmentHealthRuleStatus.Risk, 10)]
    [InlineData(24, EquipmentHealthRuleStatus.Risk, 10)]
    public void Runtime_rule_includes_twenty_productive_hour_boundary(
        double productiveHours,
        EquipmentHealthRuleStatus expectedStatus,
        int expectedPenalty)
    {
        var input = NormalInput() with
        {
            Runtime = new EquipmentHealthRuntimeFact(
                productiveHours,
                Source("runtime-rollup", "近24小时生产运行")),
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);
        var evaluation = Evaluation(result, EquipmentHealthScoringPolicy.RuntimeHoursRuleCode);

        Assert.Equal(expectedStatus, evaluation.Status);
        Assert.Equal(expectedPenalty, evaluation.Penalty);
    }

    [Fact]
    public void One_cleared_alarm_alone_is_normal()
    {
        var input = NormalInput() with
        {
            Alarms =
            [
                Alarm(EquipmentHealthAlarmSeverity.Warning, isActive: false, minutesAgo: 15),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Normal, evaluation.Status);
        Assert.Equal(0, evaluation.Penalty);
    }

    [Theory]
    [InlineData(EquipmentHealthAlarmSeverity.Other, 20)]
    [InlineData(EquipmentHealthAlarmSeverity.Warning, 45)]
    [InlineData(EquipmentHealthAlarmSeverity.Critical, 65)]
    public void Active_alarm_uses_severity_specific_penalty(
        EquipmentHealthAlarmSeverity severity,
        int expectedPenalty)
    {
        var input = NormalInput() with
        {
            Alarms = [Alarm(severity, isActive: true, minutesAgo: 5)],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Risk, evaluation.Status);
        Assert.Equal(expectedPenalty, evaluation.Penalty);
    }

    [Fact]
    public void Alarm_risk_evidence_uses_the_fact_that_determines_each_penalty()
    {
        var activeCritical = Alarm(
            EquipmentHealthAlarmSeverity.Critical,
            isActive: true,
            minutesAgo: 10);
        var newerActiveWarning = Alarm(
            EquipmentHealthAlarmSeverity.Warning,
            isActive: true,
            minutesAgo: 1);
        var criticalResult = EquipmentHealthScoringPolicy.Evaluate(
            NormalInput() with { Alarms = [activeCritical, newerActiveWarning] });

        Assert.Equal(
            activeCritical.LatestLifecycleFact,
            Evaluation(criticalResult, EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode).SourceFact);

        var activeWarning = Alarm(
            EquipmentHealthAlarmSeverity.Warning,
            isActive: true,
            minutesAgo: 10);
        var newerClearedCritical = Alarm(
            EquipmentHealthAlarmSeverity.Critical,
            isActive: false,
            minutesAgo: 1);
        var warningResult = EquipmentHealthScoringPolicy.Evaluate(
            NormalInput() with { Alarms = [activeWarning, newerClearedCritical] });

        Assert.Equal(
            activeWarning.LatestLifecycleFact,
            Evaluation(warningResult, EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode).SourceFact);

        var oldestRecentRaise = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 180);
        var middleRecentRaise = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 120);
        var newestRecentRaise = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 60);
        var irrelevantOldRaiseWithNewClear = Alarm(
            EquipmentHealthAlarmSeverity.Critical,
            isActive: false,
            minutesAgo: 1,
            raisedMinutesAgo: 1_500);
        var repeatedResult = EquipmentHealthScoringPolicy.Evaluate(
            EmptyInput() with
            {
                Alarms =
                [
                    oldestRecentRaise,
                    middleRecentRaise,
                    newestRecentRaise,
                    irrelevantOldRaiseWithNewClear,
                ],
            });

        Assert.Equal(
            newestRecentRaise.RaisedFact,
            Evaluation(repeatedResult, EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode).SourceFact);
        Assert.Equal(irrelevantOldRaiseWithNewClear.LatestLifecycleFact, repeatedResult.NewestSourceFact);
    }

    [Fact]
    public void Cleared_alarm_counts_by_raise_time_but_uses_latest_lifecycle_fact_for_freshness()
    {
        var recentClearOfOldAlarm = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 1,
            raisedMinutesAgo: 1_500);
        var input = EmptyInput() with { Alarms = [recentClearOfOldAlarm] };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);
        var evaluation = Evaluation(result, EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Normal, evaluation.Status);
        Assert.Equal(0, evaluation.Penalty);
        Assert.Contains("24小时0次", evaluation.Current, StringComparison.Ordinal);
        Assert.Equal(recentClearOfOldAlarm.LatestLifecycleFact, evaluation.SourceFact);
        Assert.Equal(recentClearOfOldAlarm.LatestLifecycleFact, result.NewestSourceFact);
        Assert.Equal(EquipmentHealthFreshness.Fresh, result.Freshness);
    }

    [Fact]
    public void Repeated_alarm_uses_newest_qualifying_raise_while_freshness_uses_later_clear()
    {
        var olderRaiseWithLatestClear = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 1,
            raisedMinutesAgo: 180);
        var middleRaise = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 2,
            raisedMinutesAgo: 120);
        var newestQualifyingRaiseWithOlderClear = Alarm(
            EquipmentHealthAlarmSeverity.Other,
            isActive: false,
            minutesAgo: 30,
            raisedMinutesAgo: 60);
        var input = EmptyInput() with
        {
            Alarms = [olderRaiseWithLatestClear, middleRaise, newestQualifyingRaiseWithOlderClear],
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);
        var evaluation = Evaluation(result, EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(20, evaluation.Penalty);
        Assert.Equal(newestQualifyingRaiseWithOlderClear.RaisedFact, evaluation.SourceFact);
        Assert.Equal(olderRaiseWithLatestClear.LatestLifecycleFact, result.NewestSourceFact);
        Assert.Equal(EquipmentHealthFreshness.Fresh, result.Freshness);
    }

    [Fact]
    public void Three_cleared_alarms_in_twenty_four_hours_are_repeated_only_risk()
    {
        var input = NormalInput() with
        {
            Alarms =
            [
                Alarm(EquipmentHealthAlarmSeverity.Warning, isActive: false, minutesAgo: 10),
                Alarm(EquipmentHealthAlarmSeverity.Warning, isActive: false, minutesAgo: 60),
                Alarm(EquipmentHealthAlarmSeverity.Critical, isActive: false, minutesAgo: 120),
                Alarm(EquipmentHealthAlarmSeverity.Critical, isActive: false, minutesAgo: 1_500),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Risk, evaluation.Status);
        Assert.Equal(20, evaluation.Penalty);
        Assert.Contains("3", evaluation.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_critical_alarm_takes_precedence_over_active_warning()
    {
        var input = NormalInput() with
        {
            Alarms =
            [
                Alarm(EquipmentHealthAlarmSeverity.Warning, isActive: true, minutesAgo: 2),
                Alarm(EquipmentHealthAlarmSeverity.Critical, isActive: true, minutesAgo: 8),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(65, evaluation.Penalty);
    }

    [Fact]
    public void Sustained_exceedance_includes_sample_span_and_eighty_percent_boundaries()
    {
        var input = NormalInput() with
        {
            RuleObservations =
            [
                Rule(
                    history: History(
                        101, 102, 103, 104, 105,
                        106, 107, 108, 50, 60)),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Risk, evaluation.Status);
        Assert.Equal(20, evaluation.Penalty);
        Assert.Contains("80", evaluation.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Sustained_exceedance_is_normal_below_eighty_percent()
    {
        var input = NormalInput() with
        {
            RuleObservations =
            [
                Rule(
                    history: History(
                        101, 102, 103, 104, 105,
                        106, 107, 50, 60, 70)),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Normal, evaluation.Status);
        Assert.Equal(0, evaluation.Penalty);
    }

    [Theory]
    [MemberData(nameof(InsufficientHistories))]
    public void Both_historical_rules_accumulate_without_penalty_when_count_or_span_is_insufficient(
        ImmutableArray<EquipmentHealthHistorySample> history)
    {
        var input = NormalInput() with { RuleObservations = [Rule(history: history)] };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);
        var sustained = Evaluation(result, EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode);
        var trend = Evaluation(result, EquipmentHealthScoringPolicy.TrendGrowthRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Accumulating, sustained.Status);
        Assert.Equal(EquipmentHealthRuleStatus.Accumulating, trend.Status);
        Assert.Equal(0, sustained.Penalty);
        Assert.Equal(0, trend.Penalty);
    }

    [Theory]
    [InlineData(EquipmentHealthRiskDirection.High, 100, 120)]
    [InlineData(EquipmentHealthRiskDirection.Low, 100, 80)]
    public void Trend_rule_includes_twenty_percent_deterioration_boundary_in_alarm_direction(
        EquipmentHealthRiskDirection direction,
        double firstThirdValue,
        double lastThirdValue)
    {
        var input = NormalInput() with
        {
            RuleObservations =
            [
                Rule(
                    direction: direction,
                    history: History(
                        firstThirdValue,
                        firstThirdValue,
                        1_000,
                        -1_000,
                        lastThirdValue,
                        lastThirdValue)),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.TrendGrowthRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Risk, evaluation.Status);
        Assert.Equal(15, evaluation.Penalty);
    }

    [Theory]
    [InlineData(EquipmentHealthRiskDirection.High, 100, 119.999)]
    [InlineData(EquipmentHealthRiskDirection.Low, 100, 80.001)]
    public void Trend_rule_is_normal_below_twenty_percent_deterioration(
        EquipmentHealthRiskDirection direction,
        double firstThirdValue,
        double lastThirdValue)
    {
        var input = NormalInput() with
        {
            RuleObservations =
            [
                Rule(
                    direction: direction,
                    history: History(
                        firstThirdValue,
                        firstThirdValue,
                        0,
                        0,
                        lastThirdValue,
                        lastThirdValue)),
            ],
        };

        var evaluation = Evaluation(
            EquipmentHealthScoringPolicy.Evaluate(input),
            EquipmentHealthScoringPolicy.TrendGrowthRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Normal, evaluation.Status);
        Assert.Equal(0, evaluation.Penalty);
    }

    [Fact]
    public void Multiple_rule_observations_select_one_riskiest_matching_rule_without_cross_tag_history()
    {
        var safeTemperature = Rule(
            ruleCode: "temperature-high",
            tagKey: "temperature",
            direction: EquipmentHealthRiskDirection.High,
            currentValue: 50,
            history: History(50, 50, 50, 50, 50, 50));
        var riskyPressure = Rule(
            ruleCode: "pressure-low",
            tagKey: "pressure",
            direction: EquipmentHealthRiskDirection.Low,
            severity: EquipmentHealthAlarmSeverity.Critical,
            thresholdValue: 10,
            unit: "bar",
            currentValue: 12,
            history: History(13, 10, 9, 9, 9, 9, 9, 9, 8, 8));
        var input = EmptyInput() with { RuleObservations = [safeTemperature, riskyPressure] };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);

        foreach (var ruleCode in new[]
                 {
                     EquipmentHealthScoringPolicy.ThresholdProximityRuleCode,
                     EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode,
                     EquipmentHealthScoringPolicy.TrendGrowthRuleCode,
                 })
        {
            var evaluation = Evaluation(result, ruleCode);
            Assert.Equal(EquipmentHealthRuleStatus.Risk, evaluation.Status);
            Assert.Contains("pressure-low", evaluation.Evidence, StringComparison.Ordinal);
            Assert.Contains("pressure", evaluation.Evidence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Trend_accumulates_without_penalty_when_no_rule_establishes_direction_and_threshold()
    {
        var result = EquipmentHealthScoringPolicy.Evaluate(EmptyInput());

        var evaluation = Evaluation(result, EquipmentHealthScoringPolicy.TrendGrowthRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Accumulating, evaluation.Status);
        Assert.Equal(0, evaluation.Penalty);
        Assert.DoesNotContain(
            result.RiskFactors,
            factor => factor.RuleCode == EquipmentHealthScoringPolicy.TrendGrowthRuleCode);
    }

    [Fact]
    public void Score_is_clamped_to_zero_when_penalties_exceed_one_hundred()
    {
        var input = NormalInput() with
        {
            RuleObservations =
            [
                Rule(
                    currentValue: 110,
                    history: History(100, 100, 101, 102, 125, 125)),
            ],
            Runtime = new EquipmentHealthRuntimeFact(
                20,
                Source("runtime-rollup", "近24小时生产运行")),
            Alarms = [Alarm(EquipmentHealthAlarmSeverity.Critical, isActive: true, minutesAgo: 1)],
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);

        Assert.Equal(0, result.Score);
        Assert.Equal(EquipmentHealthLevel.Critical, result.Level);
        Assert.Equal(5, result.RiskFactors.Length);
    }

    [Fact]
    public void Normal_score_is_clamped_at_one_hundred()
    {
        var result = EquipmentHealthScoringPolicy.Evaluate(NormalInput());

        Assert.Equal(100, result.Score);
        Assert.Equal(EquipmentHealthLevel.Healthy, result.Level);
    }

    [Theory]
    [InlineData(100, EquipmentHealthLevel.Healthy)]
    [InlineData(90, EquipmentHealthLevel.Healthy)]
    [InlineData(89, EquipmentHealthLevel.Watch)]
    [InlineData(70, EquipmentHealthLevel.Watch)]
    [InlineData(69, EquipmentHealthLevel.Warning)]
    [InlineData(40, EquipmentHealthLevel.Warning)]
    [InlineData(39, EquipmentHealthLevel.Critical)]
    [InlineData(0, EquipmentHealthLevel.Critical)]
    public void Level_boundaries_are_inclusive(int score, EquipmentHealthLevel expectedLevel)
    {
        Assert.Equal(expectedLevel, EquipmentHealthScoringPolicy.Classify(score));
    }

    [Fact]
    public void Result_returns_all_five_ordered_explainable_evaluations_and_triggered_only_risk_factors()
    {
        var input = NormalInput() with
        {
            Runtime = new EquipmentHealthRuntimeFact(
                20,
                Source("runtime-rollup", "近24小时生产运行")),
            RuleObservations = [Rule(history: History(100, 100, 50, 50, 120, 120))],
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);

        Assert.Equal(
            [
                EquipmentHealthScoringPolicy.ThresholdProximityRuleCode,
                EquipmentHealthScoringPolicy.RuntimeHoursRuleCode,
                EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode,
                EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode,
                EquipmentHealthScoringPolicy.TrendGrowthRuleCode,
            ],
            result.Evaluations.Select(evaluation => evaluation.RuleCode));
        Assert.All(
            result.Evaluations,
            evaluation =>
            {
                Assert.False(string.IsNullOrWhiteSpace(evaluation.Label));
                Assert.False(string.IsNullOrWhiteSpace(evaluation.Current));
                Assert.False(string.IsNullOrWhiteSpace(evaluation.Threshold));
                Assert.False(string.IsNullOrWhiteSpace(evaluation.Unit));
                Assert.False(string.IsNullOrWhiteSpace(evaluation.Evidence));
            });

        var threshold = result.Evaluations[0];
        Assert.Equal("telemetry-summary", threshold.SourceFact?.Type);
        Assert.Equal("温度最新值", threshold.SourceFact?.Label);
        Assert.Equal(AsOfUtc.AddMinutes(-1), threshold.SourceFact?.OccurredAtUtc);

        Assert.Equal(
            [
                EquipmentHealthScoringPolicy.RuntimeHoursRuleCode,
                EquipmentHealthScoringPolicy.TrendGrowthRuleCode,
            ],
            result.RiskFactors.Select(factor => factor.RuleCode));
        Assert.All(result.RiskFactors, factor => Assert.True(factor.Penalty > 0));
    }

    [Theory]
    [InlineData(2, EquipmentHealthFreshness.Fresh)]
    [InlineData(10, EquipmentHealthFreshness.Delayed)]
    [InlineData(10.001, EquipmentHealthFreshness.Stale)]
    public void Freshness_uses_newest_source_fact_and_includes_age_boundaries(
        double newestAgeMinutes,
        EquipmentHealthFreshness expectedFreshness)
    {
        var newestSource = new EquipmentHealthSourceFact(
            "telemetry-summary",
            "温度最新值",
            AsOfUtc.AddMinutes(-newestAgeMinutes));
        var input = EmptyInput() with
        {
            RuleObservations = [Rule(currentSource: newestSource, history: [])],
            Runtime = new EquipmentHealthRuntimeFact(
                1,
                new EquipmentHealthSourceFact(
                    "runtime-rollup",
                    "近24小时生产运行",
                    AsOfUtc.AddHours(-1))),
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);

        Assert.Equal(expectedFreshness, result.Freshness);
        Assert.Equal(newestSource, result.NewestSourceFact);
    }

    [Fact]
    public void Freshness_is_unavailable_when_no_source_fact_exists()
    {
        var result = EquipmentHealthScoringPolicy.Evaluate(EmptyInput());

        Assert.Equal(EquipmentHealthFreshness.Unavailable, result.Freshness);
        Assert.Null(result.NewestSourceFact);
    }

    [Fact]
    public void Future_rule_samples_history_and_runtime_are_ignored_for_scoring_and_freshness()
    {
        var futureSource = new EquipmentHealthSourceFact(
            "future-fact",
            "时钟超前事实",
            AsOfUtc.AddSeconds(1));
        var futureHistory = History(100, 100, 101, 102, 125, 125)
            .Select(
                (sample, index) => sample with
                {
                    SourceFact = sample.SourceFact with
                    {
                        OccurredAtUtc = AsOfUtc.AddMinutes(index + 1),
                    },
                })
            .ToImmutableArray();
        var input = EmptyInput() with
        {
            RuleObservations =
            [
                Rule(
                    currentValue: 110,
                    currentSource: futureSource,
                    history: futureHistory),
            ],
            Runtime = new EquipmentHealthRuntimeFact(24, futureSource),
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);

        Assert.Equal(100, result.Score);
        Assert.Equal(EquipmentHealthFreshness.Unavailable, result.Freshness);
        Assert.Null(result.NewestSourceFact);
        Assert.All(
            new[]
            {
                EquipmentHealthScoringPolicy.ThresholdProximityRuleCode,
                EquipmentHealthScoringPolicy.RuntimeHoursRuleCode,
                EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode,
                EquipmentHealthScoringPolicy.TrendGrowthRuleCode,
            },
            ruleCode =>
            {
                var evaluation = Evaluation(result, ruleCode);
                Assert.Equal(EquipmentHealthRuleStatus.Accumulating, evaluation.Status);
                Assert.Equal(0, evaluation.Penalty);
            });
    }

    [Fact]
    public void Future_alarm_raise_and_lifecycle_facts_are_not_active_recent_or_fresh()
    {
        var entirelyFutureActiveAlarm = Alarm(
            EquipmentHealthAlarmSeverity.Critical,
            isActive: true,
            minutesAgo: -1);
        var futureLifecycleForPastRaise = Alarm(
            EquipmentHealthAlarmSeverity.Critical,
            isActive: true,
            minutesAgo: -1,
            raisedMinutesAgo: 1);
        var input = EmptyInput() with
        {
            Alarms = [entirelyFutureActiveAlarm, futureLifecycleForPastRaise],
        };

        var result = EquipmentHealthScoringPolicy.Evaluate(input);
        var evaluation = Evaluation(result, EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);

        Assert.Equal(EquipmentHealthRuleStatus.Normal, evaluation.Status);
        Assert.Equal(0, evaluation.Penalty);
        Assert.Contains("0个活动", evaluation.Current, StringComparison.Ordinal);
        Assert.Contains("24小时1次", evaluation.Current, StringComparison.Ordinal);
        Assert.Equal(futureLifecycleForPastRaise.RaisedFact, evaluation.SourceFact);
        Assert.Equal(futureLifecycleForPastRaise.RaisedFact, result.NewestSourceFact);
        Assert.Equal(EquipmentHealthFreshness.Fresh, result.Freshness);
    }

    public static TheoryData<ImmutableArray<EquipmentHealthHistorySample>> InsufficientHistories()
    {
        return new TheoryData<ImmutableArray<EquipmentHealthHistorySample>>
        {
            History(101, 102, 103, 104, 105),
            HistoryWithSpan(TimeSpan.FromMinutes(29), 101, 102, 103, 104, 105, 106),
        };
    }

    private static EquipmentHealthScoringInput NormalInput()
    {
        return new EquipmentHealthScoringInput(
            AsOfUtc,
            [Rule()],
            new EquipmentHealthRuntimeFact(
                12,
                Source("runtime-rollup", "近24小时生产运行")),
            []);
    }

    private static EquipmentHealthScoringInput EmptyInput()
    {
        return new EquipmentHealthScoringInput(
            AsOfUtc,
            [],
            null,
            []);
    }

    private static EquipmentHealthRuleObservation Rule(
        string ruleCode = "temperature-high",
        string tagKey = "temperature",
        EquipmentHealthRiskDirection direction = EquipmentHealthRiskDirection.High,
        EquipmentHealthAlarmSeverity severity = EquipmentHealthAlarmSeverity.Warning,
        double thresholdValue = 100,
        string unit = "℃",
        double? currentValue = 50,
        EquipmentHealthSourceFact? currentSource = null,
        ImmutableArray<EquipmentHealthHistorySample>? history = null)
    {
        return new EquipmentHealthRuleObservation(
            ruleCode,
            tagKey,
            direction,
            severity,
            thresholdValue,
            unit,
            currentValue is null
                ? null
                : new EquipmentHealthValueSample(
                    currentValue.Value,
                    currentSource ?? Source("telemetry-summary", "温度最新值")),
            history ?? History(50, 50, 50, 50, 50, 50));
    }

    private static EquipmentHealthAlarmFact Alarm(
        EquipmentHealthAlarmSeverity severity,
        bool isActive,
        double minutesAgo,
        double? raisedMinutesAgo = null)
    {
        var state = isActive ? "活动" : "已清除";
        return new EquipmentHealthAlarmFact(
            severity,
            isActive,
            new EquipmentHealthSourceFact(
                "alarm-raised",
                $"{severity}-触发",
                AsOfUtc.AddMinutes(-(raisedMinutesAgo ?? minutesAgo))),
            new EquipmentHealthSourceFact(
                isActive ? "alarm-raised" : "alarm-cleared",
                $"{severity}-{state}",
                AsOfUtc.AddMinutes(-minutesAgo)));
    }

    private static ImmutableArray<EquipmentHealthHistorySample> History(params double[] values)
    {
        return HistoryWithSpan(TimeSpan.FromMinutes(30), values);
    }

    private static ImmutableArray<EquipmentHealthHistorySample> HistoryWithSpan(
        TimeSpan span,
        params double[] values)
    {
        if (values.Length == 0)
        {
            return [];
        }

        var intervalTicks = values.Length == 1 ? 0 : span.Ticks / (values.Length - 1);
        return values
            .Select(
                (value, index) =>
                {
                    var occurredAtUtc = AsOfUtc.Subtract(span).AddTicks(intervalTicks * index);
                    return new EquipmentHealthHistorySample(
                        value,
                        new EquipmentHealthSourceFact(
                            "telemetry-sample",
                            "温度历史样本",
                            occurredAtUtc));
                })
            .ToImmutableArray();
    }

    private static EquipmentHealthSourceFact Source(string type, string label)
    {
        return new EquipmentHealthSourceFact(type, label, AsOfUtc.AddMinutes(-1));
    }

    private static EquipmentHealthRuleEvaluation Evaluation(
        EquipmentHealthScoringResult result,
        string ruleCode)
    {
        return Assert.Single(result.Evaluations, evaluation => evaluation.RuleCode == ruleCode);
    }
}
