using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal static class SchedulingCandidateProjector
{
    internal static SchedulingCandidateContract Project(ReschedulingCandidateInput input, ReschedulingCandidate candidate)
    {
        var context = ReschedulingCandidateContext.Create(input);
        var frozenKeys = candidate.Impact.FrozenAssignments.Select(x => ReschedulingCandidateContext.Key(x.Assignment)).ToHashSet();
        // Algorithm-only baseline preservation is not a planner lock. Both sides use the actual current freeze set.
        ScheduleAssignmentContract[] Assignments(SchedulePlanContract plan) => plan.Assignments.Select(x =>
            x with { IsLocked = frozenKeys.Contains(ReschedulingCandidateContext.Key(x)) }).ToArray();
        var baselineAssignments = Assignments(input.Baseline);
        var assignments = Assignments(candidate.Plan);
        var scheduler = new FiniteCapacityScheduler(input.MaterialMode, input.QualityMode);
        var baselineMetrics = scheduler.ProjectAssignmentMetrics(context.Problem, baselineAssignments, input.Baseline.UnscheduledOperations.Count);
        var candidateMetrics = scheduler.ProjectAssignmentMetrics(context.Problem, assignments, candidate.Plan.UnscheduledOperations.Count);
        var plan = candidate.Plan with { Assignments = assignments, Metrics = candidateMetrics };
        var due = input.Problem.Orders.SelectMany(o => o.Operations.Select(op => ((o.OrderId, op.OperationId), op.DueUtc)))
            .ToDictionary(x => x.Item1, x => x.DueUtc);
        int LateOrders(SchedulePlanContract value) => value.Assignments.Where(x => due.TryGetValue((x.OrderId, x.OperationId), out var time)
            && x.EndUtc > time).Select(x => x.OrderId).Distinct(StringComparer.Ordinal).Count();
        var baselineLate = LateOrders(input.Baseline);
        var candidateLate = LateOrders(plan);
        var byKey = assignments.ToDictionary(ReschedulingCandidateContext.Key);
        var locked = candidate.Impact.FrozenAssignments.Select(x =>
        {
            byKey.TryGetValue(ReschedulingCandidateContext.Key(x.Assignment), out var value);
            return new SchedulingCandidateLockPreservationContract(x.Assignment, value, value is not null && !Changed(x.Assignment, value));
        }).ToArray();
        var baselineUnscheduled = input.Baseline.UnscheduledOperations.Count;
        var candidateUnscheduled = plan.UnscheduledOperations.Count;
        var kpis = new SchedulingCandidateKpisContract(baselineMetrics.OnTimeRate, candidateMetrics.OnTimeRate,
            candidateMetrics.OnTimeRate - baselineMetrics.OnTimeRate, baselineMetrics.OptimizableOperationCount, candidateMetrics.OptimizableOperationCount,
            baselineLate, candidateLate, candidateLate - baselineLate, candidate.Movements.Count,
            baselineMetrics.AverageResourceUtilization, candidateMetrics.AverageResourceUtilization,
            candidateMetrics.AverageResourceUtilization - baselineMetrics.AverageResourceUtilization,
            baselineUnscheduled, candidateUnscheduled, candidateUnscheduled - baselineUnscheduled,
            locked.Count(x => x.Preserved), locked.Length, locked);
        var explanations = candidate.Explanations.Select(x => new SchedulingCandidateExplanationContract(x.OrderId, x.OperationId,
            x.Code, Reasons(x.Reasons), Paths(x.Paths))).ToList();
        foreach (var unscheduled in plan.UnscheduledOperations.Where(x => !explanations.Any(e => e.OrderId == x.OrderId && e.OperationId == x.OperationId)))
            explanations.Add(new(unscheduled.OrderId, unscheduled.OperationId, unscheduled.ReasonCode.ToString(), [], []));
        var baselineKeys = input.Baseline.Assignments.Select(ReschedulingCandidateContext.Key)
            .Concat(input.Baseline.UnscheduledOperations.Select(x => (x.OrderId, x.OperationId))).ToHashSet();
        foreach (var op in input.Problem.Orders.SelectMany(o => o.Operations.Select(x => (o.OrderId, x.OperationId)))
            .Where(x => !baselineKeys.Contains(x)))
            explanations.Add(new(op.OrderId, op.OperationId, "new-operation", [], []));
        return new(SchedulingReschedulingStrategyContract.RightShift, candidate.InputFingerprint, plan, kpis,
            candidate.Movements.Select(x => new SchedulingCandidateMovementContract(x.Original,
                byKey[ReschedulingCandidateContext.Key(x.Candidate)], Reasons(x.Reasons), Paths(x.Paths))).ToArray(), explanations,
            candidate.Transfers.Select(x => new SchedulingCandidateTransferContract(x.OrderId, x.OperationId,
                x.OriginalResourceId, x.ResourceId, x.SetupMinutes, x.DeviceSources.Select(d => new SchedulingCandidateDeviceSourceContract(
                    d.ResourceId, d.SubstituteResourceId, d.Source.Window.SourceReferenceId, d.Source.Window.ReasonCode)).ToArray())).ToArray());
    }

    private static bool Changed(ScheduleAssignmentContract a, ScheduleAssignmentContract b) => a.ResourceId != b.ResourceId
        || a.StartUtc != b.StartUtc || a.EndUtc != b.EndUtc || !(a.Segments ?? []).OrderBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc).SequenceEqual((b.Segments ?? []).OrderBy(x => x.StartUtc).ThenBy(x => x.EndUtc));
    private static SchedulingCandidateReasonContract[] Reasons(IReadOnlyList<ReschedulingImpactReason> reasons) => reasons
        .Select(x => new SchedulingCandidateReasonContract((SchedulingCandidateReasonCodeContract)x.Code, Source(x.Source))).ToArray();
    private static SchedulingCandidatePathContract[] Paths(IReadOnlyList<ReschedulingImpactPath> paths) => paths.Select(x =>
        new SchedulingCandidatePathContract(Source(x.Source), new(x.Root.OrderId, x.Root.OperationId), x.Steps.Select(s =>
            new SchedulingCandidateStepContract(new(s.From.OrderId, s.From.OperationId), new(s.To.OrderId, s.To.OperationId),
                (SchedulingCandidateReasonCodeContract)s.Code, s.CompetitionWindow, s.CapacityUnits)).ToArray())).ToArray();
    private static SchedulingCandidateDeviationContract Source(SchedulingDeviation source) => source switch
    {
        SchedulingResourceUnavailableDeviation x => new(SchedulingCandidateDeviationKindContract.ResourceUnavailable,
            x.SourceReference, x.SourceVersion, x.OccurredAtUtc, x.ReasonCode, x.ResourceId, x.StartUtc, x.EndUtc),
        SchedulingOperationDeviation x => new(SchedulingCandidateDeviationKindContract.OperationDeviation,
            x.SourceReference, x.SourceVersion, x.OccurredAtUtc, x.ReasonCode, OrderId: x.OrderId, OperationId: x.OperationId),
        SchedulingInsertedOperationDeviation x => new(SchedulingCandidateDeviationKindContract.NewOperation,
            x.SourceReference, x.SourceVersion, x.OccurredAtUtc, x.ReasonCode, OrderId: x.OrderId, OperationId: x.OperationId),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
