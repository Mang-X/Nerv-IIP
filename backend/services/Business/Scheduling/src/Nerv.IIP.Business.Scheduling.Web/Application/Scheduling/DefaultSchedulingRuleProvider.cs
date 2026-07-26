using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class DefaultSchedulingRuleProvider(
    ISchedulingOperationOverrideOverlay overrideOverlay) : ISchedulingRuleProvider
{
    public const string SourceId = "operation-overrides";
    private const int MaxSummaryReasonCodes = 16;

    public async Task<SchedulingRuleProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var effectiveProblem = await overrideOverlay.ApplyAsync(problem, cancellationToken);
        var reasonCodes = effectiveProblem.LockedAssignments
            .Select(x => x.LockReasonCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(MaxSummaryReasonCodes)
            .ToArray();

        return new SchedulingRuleProviderResult(
            effectiveProblem,
            [
                new SchedulingProviderSummary(
                    SourceId,
                    effectiveProblem.LockedAssignments.Count == 0
                        ? SchedulingProviderOutcome.NoData
                        : SchedulingProviderOutcome.Applied,
                    effectiveProblem.LockedAssignments.Count,
                    reasonCodes)
            ]);
    }
}
