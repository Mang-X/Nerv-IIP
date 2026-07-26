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
    SchedulingProviderOutcome Outcome,
    int FactCount,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record SchedulingRuleProviderResult(
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
