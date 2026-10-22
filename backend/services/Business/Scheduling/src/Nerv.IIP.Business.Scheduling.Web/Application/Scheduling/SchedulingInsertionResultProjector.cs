using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>#3620 spec r1 / ADR 0032 §5：工单完工差、严格破交期与六项 KPI 使用同一规范化输入。</summary>
internal static class SchedulingInsertionResultProjector
{
    public static SchedulingInsertionPreviewResultContract Project(SchedulingInsertionResult result,
        SchedulingInsertionCalculationSnapshotContract snapshot, string insertedOrderId, FiniteCapacityScheduler scheduler)
    {
        var baseline = snapshot.Baseline;
        var candidate = result.Candidate;
        var originalByKey = baseline.Assignments.ToDictionary(Key);
        var candidateByKey = candidate.Assignments.ToDictionary(Key);
        var frozenByKey = snapshot.Freeze.Assignments.ToDictionary(x => Key(x.Assignment));
        var dueByKey = snapshot.Problem.Orders.SelectMany(order => order.Operations.Select(operation =>
            (Key: (order.OrderId, operation.OperationId), operation.DueUtc))).ToDictionary(x => x.Key, x => x.DueUtc);
        var baselineOrderIds = snapshot.BaselineProblemOrderIds();
        var orders = snapshot.Problem.Orders.OrderBy(x => x.OrderId, StringComparer.Ordinal).Select(order =>
        {
            var oldAssignments = baseline.Assignments.Where(x => x.OrderId == order.OrderId).ToArray();
            var newAssignments = candidate.Assignments.Where(x => x.OrderId == order.OrderId).ToArray();
            var isNew = !baselineOrderIds.Contains(order.OrderId);
            var incomplete = newAssignments.Length != order.Operations.Count || candidate.UnscheduledOperations.Any(x => x.OrderId == order.OrderId);
            DateTimeOffset? oldCompletion = oldAssignments.Length == 0 || baseline.UnscheduledOperations.Any(x => x.OrderId == order.OrderId)
                ? null : oldAssignments.Max(x => x.EndUtc);
            DateTimeOffset? newCompletion = incomplete ? null : newAssignments.Max(x => x.EndUtc);
            decimal? days = isNew || incomplete || oldCompletion is null ? null : Math.Round(
                Math.Max(0m, (decimal)(newCompletion!.Value - oldCompletion.Value).TotalHours / 24m), 2);
            var oldLate = oldAssignments.Any(x => x.EndUtc > dueByKey[Key(x)]);
            var newLate = newAssignments.Any(x => x.EndUtc > dueByKey[Key(x)]);
            return new SchedulingInsertionOrderImpactContract(order.OrderId, isNew,
                incomplete ? SchedulingInsertionOrderStatusContract.Unscheduled : isNew ? SchedulingInsertionOrderStatusContract.New :
                newCompletion > oldCompletion ? SchedulingInsertionOrderStatusContract.Delayed : SchedulingInsertionOrderStatusContract.Unchanged,
                oldCompletion, newCompletion, days, oldLate, newLate, !oldLate && newLate);
        }).ToArray();
        var affectedByKey = result.Impact.AffectedOperations.ToDictionary(x => Key(x.Assignment));
        var operations = dueByKey.OrderBy(x => x.Key.OrderId, StringComparer.Ordinal).ThenBy(x => x.Key.OperationId, StringComparer.Ordinal)
            .Where(x => !originalByKey.TryGetValue(x.Key, out var old) || !candidateByKey.TryGetValue(x.Key, out var next) || Changed(old, next)
                || candidate.Conflicts.Any(c => (c.OrderId, c.OperationId) == x.Key)).Select(x =>
            {
                originalByKey.TryGetValue(x.Key, out var old);
                candidateByKey.TryGetValue(x.Key, out var next);
                affectedByKey.TryGetValue(x.Key, out var affected);
                var reasons = affected?.Reasons.Select(r => r.Code.ToString()).ToList() ?? [];
                if (x.Key.OrderId == insertedOrderId) reasons.Add("rush-insertion");
                if (next is null) reasons.Add("unscheduled");
                if (frozenByKey.ContainsKey(x.Key) && candidate.Conflicts.Any(c => (c.OrderId, c.OperationId) == x.Key)) reasons.Add("frozen-conflict");
                if (reasons.Count == 0) reasons.Add(next?.ExplanationCode ?? "unscheduled");
                IReadOnlyCollection<IReadOnlyCollection<SchedulingInsertionPropagationStepContract>> paths = affected?.Paths.Select(path =>
                    (IReadOnlyCollection<SchedulingInsertionPropagationStepContract>)path.Steps.Select(step => new SchedulingInsertionPropagationStepContract(
                        step.From.OrderId, step.From.OperationId, step.To.OrderId, step.To.OperationId, step.Code.ToString(),
                        step.CompetitionWindow, step.CapacityUnits)).ToArray()).ToArray() ?? [];
                return new SchedulingInsertionOperationImpactContract(x.Key.OrderId, x.Key.OperationId, x.Value, old, next,
                    reasons.Distinct(StringComparer.Ordinal).ToArray(), $"insertion/{insertedOrderId}", paths);
            }).ToArray();
        SchedulePlanMetricsContract Metrics(SchedulePlanContract plan) => scheduler.ScheduleNormalized(
            snapshot.Problem with { LockedAssignments = [] }, "comparison", snapshot.Freeze.AsOfUtc,
            preservedAssignments: plan.Assignments.Select(x => frozenByKey.ContainsKey(Key(x)) ? x with { IsLocked = true } : x).ToArray(),
            selectedOperations: new HashSet<(string, string)>()).Metrics;
        var before = Metrics(baseline);
        var after = Metrics(candidate);
        var locked = baseline.Assignments.Where(x => x.IsLocked || frozenByKey.ContainsKey(Key(x))).ToArray();
        var notPreserved = locked.Where(x => !candidateByKey.TryGetValue(Key(x), out var next) || Changed(x, next))
            .Select(x => new SchedulingInsertionOperationKeyContract(x.OrderId, x.OperationId)).ToArray();
        var oldLateCount = orders.Count(x => x.BaselineLate);
        var newLateCount = orders.Count(x => x.CandidateLate);
        var kpis = new SchedulingInsertionKpisContract(
            new(before.OnTimeRate, after.OnTimeRate, after.OnTimeRate - before.OnTimeRate, before.OptimizableOperationCount, after.OptimizableOperationCount),
            new(oldLateCount, newLateCount, newLateCount - oldLateCount),
            baseline.Assignments.Count(x => candidateByKey.TryGetValue(Key(x), out var next) && Changed(x, next) &&
                (!frozenByKey.TryGetValue(Key(x), out var frozen) || !frozen.Reasons.Any(r => r is SchedulePlanFreezeReasonContract.Started or SchedulePlanFreezeReasonContract.Completed))),
            new(before.AverageResourceUtilization, after.AverageResourceUtilization, after.AverageResourceUtilization - before.AverageResourceUtilization),
            new(baseline.UnscheduledOperations.Count, candidate.UnscheduledOperations.Count, candidate.UnscheduledOperations.Count - baseline.UnscheduledOperations.Count),
            new(locked.Length - notPreserved.Length, locked.Length, notPreserved));
        return new(1, baseline.PlanId, candidate.PlanId, result.InputFingerprint, candidate, result.PromiseUtc,
            result.Failures.Select(x => (SchedulingInsertionFailureContract)x).ToArray(), snapshot, kpis, orders, operations);
    }

    private static HashSet<string> BaselineProblemOrderIds(this SchedulingInsertionCalculationSnapshotContract snapshot) =>
        snapshot.Baseline.Assignments.Select(x => x.OrderId).Concat(snapshot.Baseline.UnscheduledOperations.Select(x => x.OrderId)).ToHashSet(StringComparer.Ordinal);
    private static (string OrderId, string OperationId) Key(ScheduleAssignmentContract assignment) => (assignment.OrderId, assignment.OperationId);
    private static bool Changed(ScheduleAssignmentContract a, ScheduleAssignmentContract b) => a.ResourceId != b.ResourceId ||
        a.StartUtc != b.StartUtc || a.EndUtc != b.EndUtc || !(a.Segments ?? []).SequenceEqual(b.Segments ?? []);
}
