using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public interface ISchedulingEngine
{
    string EngineId { get; }

    string Version { get; }

    SchedulePlanContract Schedule(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc);
}

public interface ISchedulingRuleProvider
{
    Task<SchedulingRuleProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);
}

public interface ISchedulingConstraintProvider
{
    Task<SchedulingConstraintProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);
}

public enum SchedulingProviderOutcome
{
    Applied,
    NoData,
    Degraded
}

public sealed record SchedulingProviderSummary(
    string SourceId,
    string SourceVersion,
    SchedulingProviderOutcome Outcome,
    int FactCount,
    string FactsFingerprint,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record SchedulingRuleProviderResult(
    string ProviderId,
    string ProfileId,
    string ProfileVersion,
    SchedulingProblemContract EffectiveProblem,
    IReadOnlyCollection<SchedulingProviderSummary> Summaries);

public sealed record SchedulingConstraintProviderResult(
    SchedulingProblemContract BaseProblem,
    SchedulingProblemContract EffectiveProblem,
    IReadOnlyCollection<SchedulingProviderSummary> Summaries);

public sealed record SchedulingPlanGenerationResult(
    SchedulePlanContract Plan,
    SchedulingRuleProviderResult Rules,
    SchedulingConstraintProviderResult Constraints,
    string EngineId,
    string EngineVersion);

internal static class SchedulingFactsFingerprint
{
    public static string Create<T>(
        IEnumerable<T> facts,
        Func<T, T>? normalize = null)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var canonicalFacts = facts
            .Select(x => normalize is null ? x : normalize(x))
            .Select(x => JsonSerializer.Serialize(x, SchedulingJson.Options))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var canonicalJson = JsonSerializer.Serialize(canonicalFacts, SchedulingJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
    }
}
