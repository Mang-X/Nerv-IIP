using System.Collections.Immutable;
using System.Globalization;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.EquipmentHealth;

public enum EquipmentHealthRiskDirection
{
    High,
    Low,
}

public enum EquipmentHealthAlarmSeverity
{
    Other,
    Warning,
    Critical,
}

public enum EquipmentHealthRuleStatus
{
    Normal,
    Risk,
    Accumulating,
}

public enum EquipmentHealthLevel
{
    Healthy,
    Watch,
    Warning,
    Critical,
}

public enum EquipmentHealthFreshness
{
    Fresh,
    Delayed,
    Stale,
    Unavailable,
}

public sealed record EquipmentHealthSourceFact(
    string Type,
    string Label,
    DateTimeOffset OccurredAtUtc);

public sealed record EquipmentHealthValueSample(
    double Value,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthRuleObservation(
    string RuleCode,
    string TagKey,
    EquipmentHealthRiskDirection Direction,
    EquipmentHealthAlarmSeverity AlarmSeverity,
    double ThresholdValue,
    string Unit,
    EquipmentHealthValueSample? CurrentSample,
    ImmutableArray<EquipmentHealthHistorySample> History);

public sealed record EquipmentHealthRuntimeFact(
    double ProductiveHours,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthAlarmFact(
    EquipmentHealthAlarmSeverity Severity,
    bool IsActive,
    EquipmentHealthSourceFact RaisedFact,
    EquipmentHealthSourceFact LatestLifecycleFact);

public sealed record EquipmentHealthHistorySample(
    double Value,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthScoringInput(
    DateTimeOffset EvaluatedAtUtc,
    ImmutableArray<EquipmentHealthRuleObservation> RuleObservations,
    EquipmentHealthRuntimeFact? Runtime,
    ImmutableArray<EquipmentHealthAlarmFact> Alarms);

public sealed record EquipmentHealthRuleEvaluation(
    string RuleCode,
    string Label,
    EquipmentHealthRuleStatus Status,
    int Penalty,
    string Current,
    string Threshold,
    string Unit,
    string Evidence,
    EquipmentHealthSourceFact? SourceFact);

public sealed record EquipmentHealthRiskFactor(
    string RuleCode,
    string Label,
    int Penalty,
    string Evidence,
    EquipmentHealthSourceFact? SourceFact);

public sealed record EquipmentHealthScoringResult(
    int Score,
    EquipmentHealthLevel Level,
    EquipmentHealthFreshness Freshness,
    EquipmentHealthSourceFact? NewestSourceFact,
    ImmutableArray<EquipmentHealthRuleEvaluation> Evaluations,
    ImmutableArray<EquipmentHealthRiskFactor> RiskFactors);

public static class EquipmentHealthScoringPolicy
{
    public const string ThresholdProximityRuleCode = "threshold-proximity";
    public const string RuntimeHoursRuleCode = "runtime-hours-24h";
    public const string AlarmFrequencyRuleCode = "alarm-frequency-24h";
    public const string SustainedExceedanceRuleCode = "sustained-exceedance";
    public const string TrendGrowthRuleCode = "trend-growth";

    private const int ThresholdProximityPenalty = 15;
    private const int RuntimeHoursPenalty = 10;
    private const int RepeatedAlarmPenalty = 20;
    private const int ActiveWarningAlarmPenalty = 45;
    private const int ActiveCriticalAlarmPenalty = 65;
    private const int SustainedExceedancePenalty = 20;
    private const int TrendGrowthPenalty = 15;

    private const int MinimumHistoricalSamples = 6;
    private const double HistoricalBreachRatio = 0.8;
    private const double DeteriorationRatio = 0.2;

    private static readonly TimeSpan MinimumHistoricalSpan = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AlarmWindow = TimeSpan.FromHours(24);

    public static EquipmentHealthScoringResult Evaluate(EquipmentHealthScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var evaluations = ImmutableArray.Create(
            EvaluateThresholdProximity(input),
            EvaluateRuntimeHours(input),
            EvaluateAlarmFrequency(input),
            EvaluateSustainedExceedance(input),
            EvaluateTrendGrowth(input));
        var riskFactors = evaluations
            .Where(evaluation => evaluation.Status == EquipmentHealthRuleStatus.Risk)
            .Select(
                evaluation => new EquipmentHealthRiskFactor(
                    evaluation.RuleCode,
                    evaluation.Label,
                    evaluation.Penalty,
                    evaluation.Evidence,
                    evaluation.SourceFact))
            .ToImmutableArray();
        var score = Math.Clamp(100 - riskFactors.Sum(factor => factor.Penalty), 0, 100);
        var newestSourceFact = FindNewestSourceFact(input);

        return new EquipmentHealthScoringResult(
            score,
            Classify(score),
            ClassifyFreshness(input.EvaluatedAtUtc, newestSourceFact),
            newestSourceFact,
            evaluations,
            riskFactors);
    }

    public static EquipmentHealthLevel Classify(int score)
    {
        var clampedScore = Math.Clamp(score, 0, 100);
        return clampedScore switch
        {
            >= 90 => EquipmentHealthLevel.Healthy,
            >= 70 => EquipmentHealthLevel.Watch,
            >= 40 => EquipmentHealthLevel.Warning,
            _ => EquipmentHealthLevel.Critical,
        };
    }

    private static EquipmentHealthRuleEvaluation EvaluateThresholdProximity(
        EquipmentHealthScoringInput input)
    {
        const string label = "阈值接近度";
        var candidates = RuleObservations(input)
            .Where(observation => observation.CurrentSample is not null)
            .Select(
                observation =>
                {
                    var safeSideDistance = SafeSideDistance(
                        observation.CurrentSample!.Value,
                        observation.ThresholdValue,
                        observation.Direction);
                    var proximityBoundary =
                        DeteriorationRatio * Math.Max(Math.Abs(observation.ThresholdValue), 1);
                    return new
                    {
                        Observation = observation,
                        SafeSideDistance = safeSideDistance,
                        ProximityBoundary = proximityBoundary,
                        IsRisk = safeSideDistance <= proximityBoundary,
                    };
                })
            .OrderByDescending(candidate => candidate.IsRisk)
            .ThenByDescending(candidate => SeverityRank(candidate.Observation.AlarmSeverity))
            .ThenBy(candidate => candidate.SafeSideDistance / candidate.ProximityBoundary)
            .ThenBy(candidate => candidate.Observation.RuleCode, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Observation.TagKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (candidates.IsDefaultOrEmpty)
        {
            return Accumulating(
                ThresholdProximityRuleCode,
                label,
                "无当前值",
                "距离阈值不超过20%",
                "—",
                "尚无带当前样本的规则观察，继续积累。",
                null);
        }

        var selected = candidates[0];
        var observation = selected.Observation;
        var directionText =
            observation.Direction == EquipmentHealthRiskDirection.High ? "上限" : "下限";
        var evidencePrefix = ObservationIdentity(observation);
        var evidence = selected.IsRisk
            ? $"{evidencePrefix} 当前值距{directionText}的安全侧距离 {Format(selected.SafeSideDistance)}，"
                + $"不超过边界 {Format(selected.ProximityBoundary)}。"
            : $"{evidencePrefix} 当前值距{directionText}的安全侧距离 {Format(selected.SafeSideDistance)}，"
                + $"超过边界 {Format(selected.ProximityBoundary)}。";

        return Evaluation(
            ThresholdProximityRuleCode,
            label,
            selected.IsRisk,
            ThresholdProximityPenalty,
            Format(observation.CurrentSample!.Value),
            Format(observation.ThresholdValue),
            observation.Unit,
            evidence,
            observation.CurrentSample.SourceFact);
    }

    private static EquipmentHealthRuleEvaluation EvaluateRuntimeHours(
        EquipmentHealthScoringInput input)
    {
        const string label = "近24小时生产运行时长";
        if (input.Runtime is null
            || input.Runtime.SourceFact.OccurredAtUtc > input.EvaluatedAtUtc)
        {
            return Accumulating(
                RuntimeHoursRuleCode,
                label,
                "无运行汇总",
                "20",
                "小时",
                "尚无近24小时生产运行事实，继续积累。",
                null);
        }

        var isRisk = input.Runtime.ProductiveHours >= 20;
        var evidence = isRisk
            ? $"近24小时生产运行 {Format(input.Runtime.ProductiveHours)} 小时，达到或超过20小时。"
            : $"近24小时生产运行 {Format(input.Runtime.ProductiveHours)} 小时，低于20小时。";

        return Evaluation(
            RuntimeHoursRuleCode,
            label,
            isRisk,
            RuntimeHoursPenalty,
            Format(input.Runtime.ProductiveHours),
            "20",
            "小时",
            evidence,
            input.Runtime.SourceFact);
    }

    private static EquipmentHealthRuleEvaluation EvaluateAlarmFrequency(
        EquipmentHealthScoringInput input)
    {
        const string label = "近24小时报警频次";
        var alarms = input.Alarms.IsDefault ? [] : input.Alarms;
        var activeAlarms = alarms
            .Where(
                alarm => alarm.IsActive
                    && alarm.RaisedFact.OccurredAtUtc <= input.EvaluatedAtUtc
                    && alarm.LatestLifecycleFact.OccurredAtUtc <= input.EvaluatedAtUtc)
            .ToImmutableArray();
        var windowStartUtc = input.EvaluatedAtUtc.Subtract(AlarmWindow);
        var alarmsInWindow = alarms
            .Where(
                alarm => alarm.RaisedFact.OccurredAtUtc >= windowStartUtc
                    && alarm.RaisedFact.OccurredAtUtc <= input.EvaluatedAtUtc)
            .ToImmutableArray();

        int penalty;
        EquipmentHealthSourceFact? sourceFact;
        string evidence;
        var activeCritical = NewestAlarm(
            activeAlarms.Where(alarm => alarm.Severity == EquipmentHealthAlarmSeverity.Critical));
        var activeWarning = NewestAlarm(
            activeAlarms.Where(alarm => alarm.Severity == EquipmentHealthAlarmSeverity.Warning));
        var activeOther = NewestAlarm(
            activeAlarms.Where(alarm => alarm.Severity == EquipmentHealthAlarmSeverity.Other));
        if (activeCritical is not null)
        {
            penalty = ActiveCriticalAlarmPenalty;
            sourceFact = activeCritical.LatestLifecycleFact;
            evidence = $"存在活动严重报警；近24小时共 {alarmsInWindow.Length} 次报警。";
        }
        else if (activeWarning is not null)
        {
            penalty = ActiveWarningAlarmPenalty;
            sourceFact = activeWarning.LatestLifecycleFact;
            evidence = $"存在活动警告报警；近24小时共 {alarmsInWindow.Length} 次报警。";
        }
        else if (activeOther is not null)
        {
            penalty = RepeatedAlarmPenalty;
            sourceFact = activeOther.LatestLifecycleFact;
            evidence = $"存在活动一般报警；近24小时共 {alarmsInWindow.Length} 次报警。";
        }
        else if (alarmsInWindow.Length >= 3)
        {
            penalty = RepeatedAlarmPenalty;
            sourceFact = alarmsInWindow
                .OrderByDescending(alarm => alarm.RaisedFact.OccurredAtUtc)
                .First()
                .RaisedFact;
            evidence = $"无活动报警，但近24小时发生 {alarmsInWindow.Length} 次报警，达到重复报警门槛。";
        }
        else
        {
            penalty = 0;
            sourceFact = NewestAlarmSourceFact(alarms, input.EvaluatedAtUtc);
            evidence = $"无活动报警，近24小时发生 {alarmsInWindow.Length} 次报警，未达到3次门槛。";
        }

        var isRisk = penalty > 0;
        var current = $"{activeAlarms.Length}个活动，24小时{alarmsInWindow.Length}次";

        return new EquipmentHealthRuleEvaluation(
            AlarmFrequencyRuleCode,
            label,
            isRisk ? EquipmentHealthRuleStatus.Risk : EquipmentHealthRuleStatus.Normal,
            penalty,
            current,
            "任一活动或24小时≥3次",
            "次",
            evidence,
            sourceFact);
    }

    private static EquipmentHealthRuleEvaluation EvaluateSustainedExceedance(
        EquipmentHealthScoringInput input)
    {
        const string label = "持续超限";
        var observations = RuleObservations(input);
        if (observations.IsDefaultOrEmpty)
        {
            return Accumulating(
                SustainedExceedanceRuleCode,
                label,
                "无规则观察",
                "≥6个/≥30分钟/超限≥80%",
                "样本",
                "缺少建立阈值与风险方向的规则观察，继续积累。",
                null);
        }

        var candidates = observations
            .Select(
                observation =>
                {
                    var history = OrderedHistory(observation.History);
                    var breachCount = history.Count(
                        sample => IsThresholdBreach(
                            sample.Value,
                            observation.ThresholdValue,
                            observation.Direction));
                    var breachRatio = history.IsDefaultOrEmpty
                        ? 0
                        : (double)breachCount / history.Length;
                    return new
                    {
                        Observation = observation,
                        History = history,
                        BreachCount = breachCount,
                        BreachRatio = breachRatio,
                        IsSufficient = HasSufficientHistory(history),
                    };
                })
            .ToImmutableArray();
        var sufficientCandidates = candidates
            .Where(candidate => candidate.IsSufficient)
            .OrderByDescending(candidate => candidate.BreachRatio >= HistoricalBreachRatio)
            .ThenByDescending(candidate => SeverityRank(candidate.Observation.AlarmSeverity))
            .ThenByDescending(candidate => candidate.BreachRatio)
            .ThenBy(candidate => candidate.Observation.RuleCode, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Observation.TagKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (sufficientCandidates.IsDefaultOrEmpty)
        {
            var selectedInsufficient = candidates
                .OrderByDescending(candidate => candidate.History.Length)
                .ThenByDescending(candidate => HistorySpan(candidate.History))
                .ThenBy(candidate => candidate.Observation.RuleCode, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Observation.TagKey, StringComparer.Ordinal)
                .First();
            return HistoricalAccumulating(
                SustainedExceedanceRuleCode,
                label,
                selectedInsufficient.Observation,
                selectedInsufficient.History);
        }

        var selected = sufficientCandidates[0];
        var isRisk = selected.BreachRatio >= HistoricalBreachRatio;
        var evidence =
            $"{ObservationIdentity(selected.Observation)} {selected.History.Length}个样本覆盖 "
            + $"{Format(HistorySpan(selected.History).TotalMinutes)} 分钟，"
            + $"{selected.BreachCount}个超限，占 {Format(selected.BreachRatio * 100)}%。";

        return Evaluation(
            SustainedExceedanceRuleCode,
            label,
            isRisk,
            SustainedExceedancePenalty,
            $"{Format(selected.BreachRatio * 100)}%",
            "80%",
            "%",
            evidence,
            NewestHistorySource(selected.History));
    }

    private static EquipmentHealthRuleEvaluation EvaluateTrendGrowth(
        EquipmentHealthScoringInput input)
    {
        const string label = "趋势恶化";
        var observations = RuleObservations(input);
        if (observations.IsDefaultOrEmpty)
        {
            return Accumulating(
                TrendGrowthRuleCode,
                label,
                "无规则观察",
                "20%",
                "%",
                "缺少建立风险方向与阈值的规则观察，继续积累且不扣分。",
                null);
        }

        var candidates = observations
            .Select(
                observation =>
                {
                    var history = OrderedHistory(observation.History);
                    if (!HasSufficientHistory(history))
                    {
                        return new
                        {
                            Observation = observation,
                            History = history,
                            FirstAverage = 0d,
                            LastAverage = 0d,
                            DeteriorationPercent = 0d,
                            IsSufficient = false,
                        };
                    }

                    var thirdLength = history.Length / 3;
                    var firstAverage = history.Take(thirdLength).Average(sample => sample.Value);
                    var lastAverage = history.TakeLast(thirdLength).Average(sample => sample.Value);
                    var deterioration = observation.Direction == EquipmentHealthRiskDirection.High
                        ? lastAverage - firstAverage
                        : firstAverage - lastAverage;
                    var deteriorationPercent =
                        deterioration / Math.Max(Math.Abs(firstAverage), 1) * 100;
                    return new
                    {
                        Observation = observation,
                        History = history,
                        FirstAverage = firstAverage,
                        LastAverage = lastAverage,
                        DeteriorationPercent = deteriorationPercent,
                        IsSufficient = true,
                    };
                })
            .ToImmutableArray();
        var sufficientCandidates = candidates
            .Where(candidate => candidate.IsSufficient)
            .OrderByDescending(candidate => candidate.DeteriorationPercent >= 20)
            .ThenByDescending(candidate => SeverityRank(candidate.Observation.AlarmSeverity))
            .ThenByDescending(candidate => candidate.DeteriorationPercent)
            .ThenBy(candidate => candidate.Observation.RuleCode, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Observation.TagKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (sufficientCandidates.IsDefaultOrEmpty)
        {
            var selectedInsufficient = candidates
                .OrderByDescending(candidate => candidate.History.Length)
                .ThenByDescending(candidate => HistorySpan(candidate.History))
                .ThenBy(candidate => candidate.Observation.RuleCode, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Observation.TagKey, StringComparer.Ordinal)
                .First();
            return HistoricalAccumulating(
                TrendGrowthRuleCode,
                label,
                selectedInsufficient.Observation,
                selectedInsufficient.History);
        }

        var selected = sufficientCandidates[0];
        var isRisk = selected.DeteriorationPercent >= 20;
        var evidence =
            $"{ObservationIdentity(selected.Observation)} 首段均值 {Format(selected.FirstAverage)}，"
            + $"末段均值 {Format(selected.LastAverage)}，"
            + $"报警风险方向恶化 {Format(selected.DeteriorationPercent)}%。";

        return Evaluation(
            TrendGrowthRuleCode,
            label,
            isRisk,
            TrendGrowthPenalty,
            $"{Format(selected.DeteriorationPercent)}%",
            "20%",
            "%",
            evidence,
            NewestHistorySource(selected.History));
    }

    private static EquipmentHealthRuleEvaluation HistoricalAccumulating(
        string ruleCode,
        string label,
        EquipmentHealthRuleObservation observation,
        ImmutableArray<EquipmentHealthHistorySample> history)
    {
        var span = HistorySpan(history);
        return Accumulating(
            ruleCode,
            label,
            $"{history.Length}个/{Format(span.TotalMinutes)}分钟",
            "≥6个/≥30分钟",
            "样本/分钟",
            $"{ObservationIdentity(observation)} 历史仅有 {history.Length} 个样本、覆盖 "
                + $"{Format(span.TotalMinutes)} 分钟，继续积累且不扣分。",
            NewestHistorySource(history));
    }

    private static EquipmentHealthRuleEvaluation Evaluation(
        string ruleCode,
        string label,
        bool isRisk,
        int riskPenalty,
        string current,
        string threshold,
        string unit,
        string evidence,
        EquipmentHealthSourceFact? sourceFact)
    {
        return new EquipmentHealthRuleEvaluation(
            ruleCode,
            label,
            isRisk ? EquipmentHealthRuleStatus.Risk : EquipmentHealthRuleStatus.Normal,
            isRisk ? riskPenalty : 0,
            current,
            threshold,
            unit,
            evidence,
            sourceFact);
    }

    private static EquipmentHealthRuleEvaluation Accumulating(
        string ruleCode,
        string label,
        string current,
        string threshold,
        string unit,
        string evidence,
        EquipmentHealthSourceFact? sourceFact)
    {
        return new EquipmentHealthRuleEvaluation(
            ruleCode,
            label,
            EquipmentHealthRuleStatus.Accumulating,
            0,
            current,
            threshold,
            unit,
            evidence,
            sourceFact);
    }

    private static ImmutableArray<EquipmentHealthHistorySample> OrderedHistory(
        ImmutableArray<EquipmentHealthHistorySample> history)
    {
        return history.IsDefault
            ? []
            : history.OrderBy(sample => sample.SourceFact.OccurredAtUtc).ToImmutableArray();
    }

    private static bool HasSufficientHistory(
        ImmutableArray<EquipmentHealthHistorySample> history)
    {
        return history.Length >= MinimumHistoricalSamples
            && HistorySpan(history) >= MinimumHistoricalSpan;
    }

    private static TimeSpan HistorySpan(ImmutableArray<EquipmentHealthHistorySample> history)
    {
        return history.Length < 2
            ? TimeSpan.Zero
            : history[^1].SourceFact.OccurredAtUtc - history[0].SourceFact.OccurredAtUtc;
    }

    private static EquipmentHealthSourceFact? NewestHistorySource(
        ImmutableArray<EquipmentHealthHistorySample> history)
    {
        return history.IsDefaultOrEmpty ? null : history[^1].SourceFact;
    }

    private static bool IsThresholdBreach(
        double value,
        double threshold,
        EquipmentHealthRiskDirection direction)
    {
        return direction == EquipmentHealthRiskDirection.High
            ? value >= threshold
            : value <= threshold;
    }

    private static double SafeSideDistance(
        double currentValue,
        double thresholdValue,
        EquipmentHealthRiskDirection direction)
    {
        return direction == EquipmentHealthRiskDirection.High
            ? thresholdValue - currentValue
            : currentValue - thresholdValue;
    }

    private static int SeverityRank(EquipmentHealthAlarmSeverity severity)
    {
        return severity switch
        {
            EquipmentHealthAlarmSeverity.Critical => 3,
            EquipmentHealthAlarmSeverity.Warning => 2,
            _ => 1,
        };
    }

    private static string ObservationIdentity(EquipmentHealthRuleObservation observation)
    {
        return $"规则 {observation.RuleCode} / 标签 {observation.TagKey}。";
    }

    private static ImmutableArray<EquipmentHealthRuleObservation> RuleObservations(
        EquipmentHealthScoringInput input)
    {
        if (input.RuleObservations.IsDefault)
        {
            return [];
        }

        return input.RuleObservations
            .Select(
                observation => observation with
                {
                    CurrentSample = observation.CurrentSample is not null
                        && observation.CurrentSample.SourceFact.OccurredAtUtc <= input.EvaluatedAtUtc
                            ? observation.CurrentSample
                            : null,
                    History = observation.History.IsDefault
                        ? []
                        : observation.History
                            .Where(
                                sample =>
                                    sample.SourceFact.OccurredAtUtc <= input.EvaluatedAtUtc)
                            .ToImmutableArray(),
                })
            .ToImmutableArray();
    }

    private static EquipmentHealthAlarmFact? NewestAlarm(
        IEnumerable<EquipmentHealthAlarmFact> alarms)
    {
        return alarms
            .OrderByDescending(alarm => alarm.LatestLifecycleFact.OccurredAtUtc)
            .FirstOrDefault();
    }

    private static EquipmentHealthSourceFact? NewestAlarmSourceFact(
        IEnumerable<EquipmentHealthAlarmFact> alarms,
        DateTimeOffset evaluatedAtUtc)
    {
        return alarms
            .SelectMany(alarm => new[] { alarm.RaisedFact, alarm.LatestLifecycleFact })
            .Where(sourceFact => sourceFact.OccurredAtUtc <= evaluatedAtUtc)
            .OrderByDescending(sourceFact => sourceFact.OccurredAtUtc)
            .FirstOrDefault();
    }

    private static EquipmentHealthSourceFact? FindNewestSourceFact(
        EquipmentHealthScoringInput input)
    {
        var sourceFacts = Enumerable.Empty<EquipmentHealthSourceFact>();
        var observations = RuleObservations(input);
        if (!observations.IsDefaultOrEmpty)
        {
            sourceFacts = sourceFacts.Concat(
                observations
                    .Where(observation => observation.CurrentSample is not null)
                    .Select(observation => observation.CurrentSample!.SourceFact));
            sourceFacts = sourceFacts.Concat(
                observations.SelectMany(
                    observation => observation.History.IsDefault
                        ? []
                        : observation.History.Select(sample => sample.SourceFact)));
        }

        if (input.Runtime is not null
            && input.Runtime.SourceFact.OccurredAtUtc <= input.EvaluatedAtUtc)
        {
            sourceFacts = sourceFacts.Append(input.Runtime.SourceFact);
        }

        if (!input.Alarms.IsDefaultOrEmpty)
        {
            sourceFacts = sourceFacts.Concat(
                input.Alarms.SelectMany(
                    alarm => new[] { alarm.RaisedFact, alarm.LatestLifecycleFact })
                    .Where(sourceFact => sourceFact.OccurredAtUtc <= input.EvaluatedAtUtc));
        }

        return sourceFacts
            .OrderByDescending(sourceFact => sourceFact.OccurredAtUtc)
            .FirstOrDefault();
    }

    private static EquipmentHealthFreshness ClassifyFreshness(
        DateTimeOffset evaluatedAtUtc,
        EquipmentHealthSourceFact? newestSourceFact)
    {
        if (newestSourceFact is null)
        {
            return EquipmentHealthFreshness.Unavailable;
        }

        var age = evaluatedAtUtc - newestSourceFact.OccurredAtUtc;
        return age <= TimeSpan.FromMinutes(2)
            ? EquipmentHealthFreshness.Fresh
            : age <= TimeSpan.FromMinutes(10)
                ? EquipmentHealthFreshness.Delayed
                : EquipmentHealthFreshness.Stale;
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
