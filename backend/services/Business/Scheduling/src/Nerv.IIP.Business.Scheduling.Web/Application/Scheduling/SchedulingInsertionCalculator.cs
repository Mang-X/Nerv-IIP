using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal enum SchedulingInsertionFailure { UnknownMaterialEta, IncompleteChain, BlockingConflict }
internal sealed record SchedulingInsertionResult(string InputFingerprint, SchedulePlanContract Candidate,
    ReschedulingImpact Impact, DateTimeOffset? PromiseUtc, IReadOnlyList<SchedulingInsertionFailure> Failures);

/// <summary>#3620 spec r1：同一有限产能模型中的急单局部序列插入，不预留、不发布、不回退全量。</summary>
internal static class SchedulingInsertionCalculator
{
    public static SchedulingInsertionResult Calculate(FiniteCapacityScheduler scheduler, SchedulingProblemContract problem,
        SchedulePlanContract baseline, string insertedOrderId, string candidatePlanId,
        IReadOnlyCollection<SchedulingFreezeExecutionFact> execution,
        IReadOnlyCollection<(string OrderId, string OperationId)> manualLocks, SchedulingFreezePolicy policy)
    {
        var normalized = SchedulingProblemNormalizer.Normalize(problem);
        var insertedOrder = normalized.Orders.Single(x => x.OrderId == insertedOrderId);
        if (insertedOrder.Operations.Count == 0 || baseline.Assignments.Any(x => x.OrderId == insertedOrderId)
            || baseline.UnscheduledOperations.Any(x => x.OrderId == insertedOrderId))
            throw new ArgumentException("Insertion requires a complete new order outside the baseline.", nameof(insertedOrderId));
        if (baseline.ProblemId != problem.ProblemId)
            throw new ArgumentException("Insertion baseline must belong to the supplied problem.", nameof(baseline));

        var baselineAssignments = baseline.Assignments.OrderBy(x => x.OrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal).ToArray();
        var baselineKeys = baselineAssignments.Select(Key).ToHashSet();
        var lockedKeys = normalized.LockedAssignments.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var allManualLocks = manualLocks.Concat(lockedKeys).Concat(baselineAssignments.Where(x => x.IsLocked).Select(Key)).Distinct().ToArray();
        var freeze = SchedulingFreezeCalculator.Calculate(baselineAssignments, execution, allManualLocks, policy);
        var insertedKeys = insertedOrder.Operations.Select(x => (insertedOrderId, x.OperationId)).ToHashSet();
        // 只把冻结项作为种子的不可移动占用；可移动队列的竞争由影响传播定位。
        var computeProblem = normalized with { LockedAssignments = normalized.LockedAssignments.Where(x => !baselineKeys.Contains((x.OrderId, x.OperationId))).ToArray() };
        var seed = scheduler.ScheduleNormalized(computeProblem, candidatePlanId, policy.AsOfUtc,
            preservedAssignments: freeze.Select(x => x.Assignment).ToArray(), selectedOperations: insertedKeys);
        var deviations = seed.Assignments.Where(x => x.OrderId == insertedOrderId)
            .Select(x => (SchedulingDeviation)new SchedulingInsertedOperationDeviation($"insertion/{insertedOrderId}", "v1", policy.AsOfUtc, x.OrderId, x.OperationId)).ToArray();
        var impact = ReschedulingImpactAnalyzer.Analyze(normalized,
            [.. baselineAssignments, .. seed.Assignments.Where(x => x.OrderId == insertedOrderId)], deviations,
            execution, allManualLocks, policy);
        var recalculate = impact.RecalculateAssignments.Select(Key).Concat(insertedKeys).ToHashSet();
        var preserved = baselineAssignments.Where(x => !recalculate.Contains(Key(x))).ToArray();
        var candidate = scheduler.ScheduleNormalized(computeProblem, candidatePlanId, policy.AsOfUtc,
            preservedAssignments: preserved, selectedOperations: recalculate);
        var unscheduled = baseline.UnscheduledOperations.Concat(candidate.UnscheduledOperations).ToArray();
        candidate = candidate with
        {
            UnscheduledOperations = unscheduled,
            Metrics = candidate.Metrics with { UnscheduledOperationCount = unscheduled.Length },
        };

        var failures = new List<SchedulingInsertionFailure>();
        if (FiniteCapacityScheduler.HasUnknownMaterialEta(normalized, insertedOrderId))
            failures.Add(SchedulingInsertionFailure.UnknownMaterialEta);
        if (candidate.Assignments.Count(x => x.OrderId == insertedOrderId) != insertedOrder.Operations.Count
            || candidate.UnscheduledOperations.Any(x => x.OrderId == insertedOrderId))
            failures.Add(SchedulingInsertionFailure.IncompleteChain);
        if (candidate.Conflicts.Any(x => x.Severity == ScheduleConflictSeverityContract.Error))
            failures.Add(SchedulingInsertionFailure.BlockingConflict);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ReschedulingImpactAnalyzer.CanonicalJson(new
        {
            impact.InputFingerprint,
            BaselinePlanId = baseline.PlanId,
            BaselineUnscheduled = baseline.UnscheduledOperations,
            InsertedOrderId = insertedOrderId,
            scheduler.MaterialConstraintMode,
            scheduler.QualityConstraintMode,
        })))).ToLowerInvariant();
        candidate = candidate with { ProblemFingerprint = fingerprint };
        return new(fingerprint, candidate, impact,
            failures.Count == 0 ? candidate.Assignments.Where(x => x.OrderId == insertedOrderId).Max(x => x.EndUtc) : null, failures);
    }

    private static (string OrderId, string OperationId) Key(ScheduleAssignmentContract assignment) =>
        (assignment.OrderId, assignment.OperationId);
}
