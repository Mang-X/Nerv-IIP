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

public sealed record EquipmentHealthThresholdFact(
    double CurrentValue,
    double ThresholdValue,
    string Unit,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthRuntimeFact(
    double ProductiveHours,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthAlarmFact(
    EquipmentHealthAlarmSeverity Severity,
    bool IsActive,
    DateTimeOffset RaisedAtUtc,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthHistorySample(
    double Value,
    EquipmentHealthSourceFact SourceFact);

public sealed record EquipmentHealthScoringInput(
    DateTimeOffset EvaluatedAtUtc,
    EquipmentHealthRiskDirection Direction,
    EquipmentHealthThresholdFact? Threshold,
    EquipmentHealthRuntimeFact? Runtime,
    ImmutableArray<EquipmentHealthAlarmFact> Alarms,
    ImmutableArray<EquipmentHealthHistorySample> History);

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
        if (input.Threshold is null)
        {
            return Accumulating(
                ThresholdProximityRuleCode,
                label,
                "无当前值",
                "距离阈值不超过20%",
                "—",
                "尚无阈值事实，继续积累。",
                null);
        }

        var fact = input.Threshold;
        var safeSideDistance = input.Direction == EquipmentHealthRiskDirection.High
            ? fact.ThresholdValue - fact.CurrentValue
            : fact.CurrentValue - fact.ThresholdValue;
        var proximityBoundary = DeteriorationRatio * Math.Max(Math.Abs(fact.ThresholdValue), 1);
        var isRisk = safeSideDistance <= proximityBoundary;
        var directionText = input.Direction == EquipmentHealthRiskDirection.High ? "上限" : "下限";
        var evidence = isRisk
            ? $"当前值距{directionText}的安全侧距离 {Format(safeSideDistance)}，不超过边界 {Format(proximityBoundary)}。"
            : $"当前值距{directionText}的安全侧距离 {Format(safeSideDistance)}，超过边界 {Format(proximityBoundary)}。";

        return Evaluation(
            ThresholdProximityRuleCode,
            label,
            isRisk,
            ThresholdProximityPenalty,
            Format(fact.CurrentValue),
            Format(fact.ThresholdValue),
            fact.Unit,
            evidence,
            fact.SourceFact);
    }

    private static EquipmentHealthRuleEvaluation EvaluateRuntimeHours(
        EquipmentHealthScoringInput input)
    {
        const string label = "近24小时生产运行时长";
        if (input.Runtime is null)
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
        var activeAlarms = alarms.Where(alarm => alarm.IsActive).ToImmutableArray();
        var windowStartUtc = input.EvaluatedAtUtc.Subtract(AlarmWindow);
        var alarmsInWindow = alarms
            .Where(
                alarm => alarm.RaisedAtUtc >= windowStartUtc
                    && alarm.RaisedAtUtc <= input.EvaluatedAtUtc)
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
            sourceFact = activeCritical.SourceFact;
            evidence = $"存在活动严重报警；近24小时共 {alarmsInWindow.Length} 次报警。";
        }
        else if (activeWarning is not null)
        {
            penalty = ActiveWarningAlarmPenalty;
            sourceFact = activeWarning.SourceFact;
            evidence = $"存在活动警告报警；近24小时共 {alarmsInWindow.Length} 次报警。";
        }
        else if (activeOther is not null)
        {
            penalty = RepeatedAlarmPenalty;
            sourceFact = activeOther.SourceFact;
            evidence = $"存在活动一般报警；近24小时共 {alarmsInWindow.Length} 次报警。";
        }
        else if (alarmsInWindow.Length >= 3)
        {
            penalty = RepeatedAlarmPenalty;
            sourceFact = alarmsInWindow
                .OrderByDescending(alarm => alarm.RaisedAtUtc)
                .First()
                .SourceFact;
            evidence = $"无活动报警，但近24小时发生 {alarmsInWindow.Length} 次报警，达到重复报警门槛。";
        }
        else
        {
            penalty = 0;
            sourceFact = NewestAlarm(alarms)?.SourceFact;
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
        var history = OrderedHistory(input.History);
        var sourceFact = NewestHistorySource(history);
        if (input.Threshold is null)
        {
            return Accumulating(
                SustainedExceedanceRuleCode,
                label,
                $"{history.Length}个样本",
                "≥6个/≥30分钟/超限≥80%",
                "样本",
                "缺少阈值事实，无法判断历史样本是否超限，继续积累。",
                sourceFact);
        }

        if (!HasSufficientHistory(history))
        {
            return HistoricalAccumulating(SustainedExceedanceRuleCode, label, history, sourceFact);
        }

        var breachCount = history.Count(
            sample => IsThresholdBreach(
                sample.Value,
                input.Threshold.ThresholdValue,
                input.Direction));
        var breachRatio = (double)breachCount / history.Length;
        var isRisk = breachRatio >= HistoricalBreachRatio;
        var evidence =
            $"{history.Length}个样本覆盖 {Format(HistorySpan(history).TotalMinutes)} 分钟，"
            + $"{breachCount}个超限，占 {Format(breachRatio * 100)}%。";

        return Evaluation(
            SustainedExceedanceRuleCode,
            label,
            isRisk,
            SustainedExceedancePenalty,
            $"{Format(breachRatio * 100)}%",
            "80%",
            "%",
            evidence,
            sourceFact);
    }

    private static EquipmentHealthRuleEvaluation EvaluateTrendGrowth(
        EquipmentHealthScoringInput input)
    {
        const string label = "趋势恶化";
        var history = OrderedHistory(input.History);
        var sourceFact = NewestHistorySource(history);
        if (!HasSufficientHistory(history))
        {
            return HistoricalAccumulating(TrendGrowthRuleCode, label, history, sourceFact);
        }

        var thirdLength = history.Length / 3;
        var firstAverage = history.Take(thirdLength).Average(sample => sample.Value);
        var lastAverage = history.TakeLast(thirdLength).Average(sample => sample.Value);
        var deterioration = input.Direction == EquipmentHealthRiskDirection.High
            ? lastAverage - firstAverage
            : firstAverage - lastAverage;
        var deteriorationBoundary = DeteriorationRatio * Math.Max(Math.Abs(firstAverage), 1);
        var isRisk = deterioration >= deteriorationBoundary;
        var deteriorationPercent = deterioration / Math.Max(Math.Abs(firstAverage), 1) * 100;
        var evidence =
            $"首段均值 {Format(firstAverage)}，末段均值 {Format(lastAverage)}，"
            + $"报警风险方向恶化 {Format(deteriorationPercent)}%。";

        return Evaluation(
            TrendGrowthRuleCode,
            label,
            isRisk,
            TrendGrowthPenalty,
            $"{Format(deteriorationPercent)}%",
            "20%",
            "%",
            evidence,
            sourceFact);
    }

    private static EquipmentHealthRuleEvaluation HistoricalAccumulating(
        string ruleCode,
        string label,
        ImmutableArray<EquipmentHealthHistorySample> history,
        EquipmentHealthSourceFact? sourceFact)
    {
        var span = HistorySpan(history);
        return Accumulating(
            ruleCode,
            label,
            $"{history.Length}个/{Format(span.TotalMinutes)}分钟",
            "≥6个/≥30分钟",
            "样本/分钟",
            $"历史仅有 {history.Length} 个样本、覆盖 {Format(span.TotalMinutes)} 分钟，继续积累且不扣分。",
            sourceFact);
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

    private static EquipmentHealthAlarmFact? NewestAlarm(
        IEnumerable<EquipmentHealthAlarmFact> alarms)
    {
        return alarms
            .OrderByDescending(alarm => alarm.SourceFact.OccurredAtUtc)
            .FirstOrDefault();
    }

    private static EquipmentHealthSourceFact? FindNewestSourceFact(
        EquipmentHealthScoringInput input)
    {
        var sourceFacts = Enumerable.Empty<EquipmentHealthSourceFact>();
        if (input.Threshold is not null)
        {
            sourceFacts = sourceFacts.Append(input.Threshold.SourceFact);
        }

        if (input.Runtime is not null)
        {
            sourceFacts = sourceFacts.Append(input.Runtime.SourceFact);
        }

        if (!input.Alarms.IsDefaultOrEmpty)
        {
            sourceFacts = sourceFacts.Concat(input.Alarms.Select(alarm => alarm.SourceFact));
        }

        if (!input.History.IsDefaultOrEmpty)
        {
            sourceFacts = sourceFacts.Concat(input.History.Select(sample => sample.SourceFact));
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
