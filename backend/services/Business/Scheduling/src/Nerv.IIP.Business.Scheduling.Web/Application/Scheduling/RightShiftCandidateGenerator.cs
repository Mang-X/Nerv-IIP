using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>ADR 0032：仅重排有量化停机事实的局部集合，保留基线资源和队列；不保存或发布。</summary>
internal static class RightShiftCandidateGenerator
{
    public static ReschedulingCandidate Generate(ReschedulingCandidateInput input)
    {
        var context = ReschedulingCandidateContext.Create(input);
        var problem = context.WithMovableOperations((operation, original) => operation with
        {
            EligibleResourceIds = operation.EligibleResourceIds.Contains(original.ResourceId) ? [original.ResourceId] : [],
            PrimaryResourceId = original.ResourceId,
            EarliestStartUtc = ReschedulingCandidateContext.Max(operation.EarliestStartUtc, original.StartUtc)
        });
        var plan = new FiniteCapacityScheduler(input.MaterialMode, input.QualityMode).ScheduleRightShiftNormalized(
            problem, $"right-shift-{context.Fingerprint}", input.Policy.AsOfUtc,
            context.Impact.AffectedOperations.Select(x => x.Assignment).ToArray(), context.Preserved);
        return context.Complete(plan, []);
    }
}
