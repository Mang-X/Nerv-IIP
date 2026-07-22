namespace Nerv.IIP.Business.Scheduling.Domain.Services;

public enum OrderUrgencyLevel
{
    Normal = 0,
    Attention = 1,
    HighRisk = 2,
    Urgent = 3,
    Critical = 4,
}

public enum BusinessPriorityLevel
{
    P3 = 0,
    P2 = 1,
    P1 = 2,
    P0 = 3,
}

public enum ExecutionRiskCategory
{
    Material = 0,
    Equipment = 1,
    Quality = 2,
    Tooling = 3,
    Capacity = 4,
    DataFreshness = 5,
}

public sealed record BusinessPriorityFact(
    BusinessPriorityLevel Level,
    string Source,
    string Reason,
    DateTimeOffset SetAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    long Revision);

public sealed record ExecutionRiskFact(
    string ReasonCode,
    ExecutionRiskCategory Category,
    bool IsBlocking,
    string SourceReference,
    DateTimeOffset ObservedAtUtc);

public sealed record OrderUrgencyCalculationInput(
    string OrderId,
    string BusinessReference,
    DateTimeOffset CalculatedAtUtc,
    DateTimeOffset? DueUtc,
    TimeSpan RemainingCycle,
    BusinessPriorityFact BusinessPriority,
    IReadOnlyCollection<ExecutionRiskFact> ExecutionRisks,
    bool IsSourceMissing,
    bool IsSourceStale,
    DateTimeOffset? FactsObservedAtUtc,
    string InputFingerprint);

public sealed record BusinessPriorityContribution(
    BusinessPriorityLevel Level,
    OrderUrgencyLevel UrgencyLevel,
    IReadOnlyList<string> ReasonCodes,
    string Source,
    string Reason,
    DateTimeOffset SetAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    long Revision);

public sealed record TimeCriticalityContribution(
    OrderUrgencyLevel Level,
    IReadOnlyList<string> ReasonCodes,
    decimal? CriticalRatio,
    decimal? SlackHours,
    decimal ExpectedDelayHours,
    DateTimeOffset? DueUtc,
    DateTimeOffset EstimatedCompletionUtc,
    decimal RemainingCycleHours);

public sealed record ExecutionRiskContribution(
    OrderUrgencyLevel Level,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ExecutionRiskFact> Facts,
    DateTimeOffset? FactsObservedAtUtc,
    bool IsSourceMissing,
    bool IsSourceStale);

public sealed record OrderUrgencyCalculationResult(
    string OrderId,
    string BusinessReference,
    OrderUrgencyLevel Level,
    BusinessPriorityContribution BusinessPriority,
    TimeCriticalityContribution TimeCriticality,
    ExecutionRiskContribution ExecutionRisk,
    decimal? CriticalRatio,
    decimal? SlackHours,
    decimal ExpectedDelayHours,
    DateTimeOffset CalculatedAtUtc,
    string ModelVersion,
    string InputFingerprint);

public static class OrderUrgencyCalculator
{
    public const string ModelVersion = "order-urgency-v1";
    private const decimal ShiftHours = 8m;

    public static OrderUrgencyCalculationResult Calculate(OrderUrgencyCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.OrderId)) throw new ArgumentException("Order id is required.", nameof(input));
        if (input.RemainingCycle < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(input), "Remaining cycle cannot be negative.");

        var business = BusinessContribution(input);
        var time = TimeContribution(input);
        var execution = ExecutionContribution(input);
        var level = new[] { business.UrgencyLevel, time.Level, execution.Level }.Max();

        return new OrderUrgencyCalculationResult(
            input.OrderId.Trim(),
            string.IsNullOrWhiteSpace(input.BusinessReference) ? input.OrderId.Trim() : input.BusinessReference.Trim(),
            level,
            business,
            time,
            execution,
            time.CriticalRatio,
            time.SlackHours,
            time.ExpectedDelayHours,
            input.CalculatedAtUtc,
            ModelVersion,
            input.InputFingerprint);
    }

    private static BusinessPriorityContribution BusinessContribution(OrderUrgencyCalculationInput input)
    {
        var fact = input.BusinessPriority;
        var expired = fact.ExpiresAtUtc.HasValue && fact.ExpiresAtUtc <= input.CalculatedAtUtc;
        var urgency = expired ? OrderUrgencyLevel.Normal : fact.Level switch
        {
            BusinessPriorityLevel.P0 => OrderUrgencyLevel.Critical,
            BusinessPriorityLevel.P1 => OrderUrgencyLevel.Urgent,
            _ => OrderUrgencyLevel.Normal,
        };
        var reasonCodes = new List<string>
        {
            $"business.priority.{fact.Level.ToString().ToLowerInvariant()}"
        };
        if (expired) reasonCodes.Add("business.priority.expired");

        return new BusinessPriorityContribution(
            fact.Level,
            urgency,
            reasonCodes,
            fact.Source,
            fact.Reason,
            fact.SetAtUtc,
            fact.ExpiresAtUtc,
            fact.Revision);
    }

    private static TimeCriticalityContribution TimeContribution(OrderUrgencyCalculationInput input)
    {
        var remainingHours = DecimalHours(input.RemainingCycle);
        var estimatedCompletion = input.CalculatedAtUtc + input.RemainingCycle;
        if (!input.DueUtc.HasValue)
        {
            return new TimeCriticalityContribution(
                OrderUrgencyLevel.HighRisk,
                ["time.due.missing"],
                null,
                null,
                0m,
                null,
                estimatedCompletion,
                remainingHours);
        }

        var availableHours = DecimalHours(input.DueUtc.Value - input.CalculatedAtUtc);
        var slackHours = Round(availableHours - remainingHours);
        decimal? criticalRatio = remainingHours == 0m ? null : Round(availableHours / remainingHours, 4);
        var expectedDelay = Math.Max(0m, -slackHours);
        var reasons = new List<string>();
        var level = OrderUrgencyLevel.Normal;

        if (availableHours < 0m)
        {
            level = OrderUrgencyLevel.Urgent;
            reasons.Add("time.due.overdue");
        }
        if (slackHours < 0m)
        {
            level = OrderUrgencyLevel.Urgent;
            reasons.Add("time.slack.negative");
        }
        if (criticalRatio.HasValue && criticalRatio < 1m)
        {
            level = OrderUrgencyLevel.Urgent;
            reasons.Add("time.cr.belowOne");
        }
        if (level < OrderUrgencyLevel.HighRisk && slackHours < ShiftHours)
        {
            level = OrderUrgencyLevel.HighRisk;
            reasons.Add("time.slack.withinShift");
        }
        if (level < OrderUrgencyLevel.Attention && criticalRatio is <= 1.2m)
        {
            level = OrderUrgencyLevel.Attention;
            reasons.Add("time.cr.attention");
        }
        if (reasons.Count == 0) reasons.Add("time.withinCommitment");

        return new TimeCriticalityContribution(
            level,
            Stable(reasons),
            criticalRatio,
            slackHours,
            expectedDelay,
            input.DueUtc,
            estimatedCompletion,
            remainingHours);
    }

    private static ExecutionRiskContribution ExecutionContribution(OrderUrgencyCalculationInput input)
    {
        var facts = input.ExecutionRisks
            .OrderBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ThenBy(x => x.SourceReference, StringComparer.Ordinal)
            .Distinct()
            .ToArray();
        var reasons = facts.Select(x => x.ReasonCode).ToList();
        if (input.IsSourceMissing) reasons.Add("urgency.source.missing");
        if (input.IsSourceStale) reasons.Add("urgency.source.stale");

        var level = input.IsSourceMissing || input.IsSourceStale || facts.Any(x => x.IsBlocking)
            ? OrderUrgencyLevel.HighRisk
            : facts.Length > 0
                ? OrderUrgencyLevel.Attention
                : OrderUrgencyLevel.Normal;
        if (reasons.Count == 0) reasons.Add("execution.risk.none");

        return new ExecutionRiskContribution(
            level,
            Stable(reasons),
            facts,
            input.FactsObservedAtUtc,
            input.IsSourceMissing,
            input.IsSourceStale);
    }

    private static IReadOnlyList<string> Stable(IEnumerable<string> values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static decimal DecimalHours(TimeSpan value) => Round((decimal)value.TotalHours);
    private static decimal Round(decimal value, int digits = 3) => decimal.Round(value, digits, MidpointRounding.AwayFromZero);
}
