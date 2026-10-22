using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>ADR 0032：停机直接命中的工序转到合格备选；传播项仍按既有依赖和有限产能求槽。</summary>
internal static class ResourceTransferCandidateGenerator
{
    public static ReschedulingCandidate Generate(ReschedulingCandidateInput input)
    {
        var context = ReschedulingCandidateContext.Create(input);
        var directlyBlocked = context.Impact.AffectedOperations
            .Where(affected => affected.Reasons.Any(reason => reason.Code == ReschedulingImpactReasonCode.ResourceUnavailable))
            .Select(affected => ReschedulingCandidateContext.Key(affected.Assignment)).ToHashSet();
        var problem = context.WithMovableOperations((operation, original) => operation with
        {
            EligibleResourceIds = directlyBlocked.Contains(ReschedulingCandidateContext.Key(original))
                ? operation.EligibleResourceIds.Where(id => id != original.ResourceId).ToArray() : operation.EligibleResourceIds,
            EarliestStartUtc = ReschedulingCandidateContext.Max(operation.EarliestStartUtc, original.StartUtc)
        });
        var scheduler = new FiniteCapacityScheduler(input.MaterialMode, input.QualityMode);
        var (plan, setupMinutes) = scheduler.ScheduleTransferNormalized(problem, $"resource-transfer-{context.Fingerprint}",
            input.Policy.AsOfUtc, context.Movable, context.Preserved);
        var sources = EquipmentAvailabilitySchedulingAdapter.ToSubstituteDeviceFacts(input.EquipmentAvailability);
        var originals = context.Movable.ToDictionary(ReschedulingCandidateContext.Key);
        var transfers = plan.Assignments.Where(candidate => originals.TryGetValue(ReschedulingCandidateContext.Key(candidate), out var original)
                && original.ResourceId != candidate.ResourceId)
            .Select(candidate => new ReschedulingResourceTransfer(candidate.OrderId, candidate.OperationId,
                originals[ReschedulingCandidateContext.Key(candidate)].ResourceId, candidate.ResourceId,
                setupMinutes[ReschedulingCandidateContext.Key(candidate)],
                sources.Where(source => source.ResourceId == originals[ReschedulingCandidateContext.Key(candidate)].ResourceId
                    && source.SubstituteResourceId == candidate.ResourceId).ToArray())).ToArray();
        return context.Complete(plan, transfers);
    }
}
