using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class SchedulingPlanGenerator(
    ISchedulingRuleProvider ruleProvider,
    ISchedulingConstraintProvider constraintProvider,
    ISchedulingEngine engine)
{
    public async Task<SchedulingPlanGenerationResult> GenerateAsync(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var rules = await ruleProvider.ApplyAsync(problem, cancellationToken);
        var constraints = await constraintProvider.ApplyAsync(
            rules.EffectiveProblem,
            cancellationToken);
        ThrowIfDuplicateConstraintSourceIds(constraints.Summaries);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedEffectiveProblem = SchedulingProblemNormalizer.Normalize(
            constraints.EffectiveProblem);
        var engineId = RequiredEngineIdentity(engine.EngineId, nameof(engine.EngineId));
        var engineVersion = RequiredEngineIdentity(engine.Version, nameof(engine.Version));
        var plan = engine.Schedule(constraints.EffectiveProblem, planId, generatedAtUtc);
        ValidateEngineOutput(
            plan,
            normalizedEffectiveProblem,
            planId,
            generatedAtUtc,
            engineVersion);
        return new SchedulingPlanGenerationResult(
            plan,
            rules,
            constraints,
            engineId,
            engineVersion);
    }

    private static void ValidateEngineOutput(
        SchedulePlanContract plan,
        SchedulingProblemContract normalizedEffectiveProblem,
        string planId,
        DateTimeOffset generatedAtUtc,
        string engineVersion)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ThrowIfMismatch(
            nameof(SchedulePlanContract.PlanId),
            planId,
            plan.PlanId);
        ThrowIfMismatch(
            nameof(SchedulePlanContract.ProblemId),
            normalizedEffectiveProblem.ProblemId,
            plan.ProblemId);
        if (plan.ContractVersion != normalizedEffectiveProblem.ContractVersion)
        {
            throw OutputMismatch(
                nameof(SchedulePlanContract.ContractVersion),
                normalizedEffectiveProblem.ContractVersion,
                plan.ContractVersion);
        }

        if (!plan.GeneratedAtUtc.EqualsExact(generatedAtUtc))
        {
            throw OutputMismatch(
                nameof(SchedulePlanContract.GeneratedAtUtc),
                generatedAtUtc,
                plan.GeneratedAtUtc);
        }

        ThrowIfMismatch(
            nameof(SchedulePlanContract.ProblemFingerprint),
            CalculateFingerprint(normalizedEffectiveProblem),
            plan.ProblemFingerprint);
        ThrowIfMismatch(
            nameof(SchedulePlanContract.AlgorithmVersion),
            engineVersion,
            plan.AlgorithmVersion);
    }

    private static void ThrowIfMismatch(
        string field,
        string expected,
        string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw OutputMismatch(field, expected, actual);
        }
    }

    private static InvalidOperationException OutputMismatch<T>(
        string field,
        T expected,
        T actual)
    {
        return new InvalidOperationException(
            $"Scheduling engine output {field} does not match the invocation. " +
            $"Expected '{expected}', actual '{actual}'.");
    }

    private static string CalculateFingerprint(
        SchedulingProblemContract normalizedEffectiveProblem)
    {
        var json = JsonSerializer.Serialize(
            normalizedEffectiveProblem,
            SchedulingJson.Options);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static string RequiredEngineIdentity(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Scheduling engine {field} must be a non-empty stable identity.");
        }

        return value.Trim();
    }

    private static void ThrowIfDuplicateConstraintSourceIds(
        IReadOnlyCollection<SchedulingProviderSummary> summaries)
    {
        var duplicates = summaries
            .GroupBy(x => x.SourceId, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new InvalidOperationException(
                $"Duplicate scheduling constraint source IDs are not allowed: {string.Join(",", duplicates)}.");
        }
    }
}
