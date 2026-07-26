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

        var plan = engine.Schedule(constraints.EffectiveProblem, planId, generatedAtUtc);
        return new SchedulingPlanGenerationResult(
            plan,
            rules,
            constraints,
            engine.EngineId,
            engine.Version);
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
