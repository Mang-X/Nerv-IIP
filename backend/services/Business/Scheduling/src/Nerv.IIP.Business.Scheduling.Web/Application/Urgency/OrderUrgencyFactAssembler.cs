using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public static class OrderUrgencyFactAssembler
{
    public static OrderUrgencyCalculationResult Calculate(
        SchedulingProblemContract problem,
        SchedulePlanContract plan,
        string orderId,
        BusinessPriorityFact businessPriority,
        DateTimeOffset calculatedAtUtc,
        string inputFingerprint,
        bool isSourceStale = false)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(plan);
        var order = problem.Orders.SingleOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Order '{orderId}' is not present in the scheduling problem.", nameof(orderId));
        var operationIds = order.Operations.Select(x => x.OperationId).ToHashSet(StringComparer.Ordinal);
        var resourceIds = order.Operations.SelectMany(x => x.EligibleResourceIds).ToHashSet(StringComparer.Ordinal);
        var workCenterIds = problem.Resources
            .Where(x => resourceIds.Contains(x.ResourceId))
            .Select(x => x.WorkCenterId)
            .ToHashSet(StringComparer.Ordinal);
        var risks = new List<ExecutionRiskFact>();

        foreach (var readiness in problem.MaterialReadiness.Where(x => Applies(x.ScopeType, x.ScopeId, orderId, operationIds, resourceIds, workCenterIds)))
        {
            if (readiness.IsReady) continue;
            var reasons = readiness.ReasonCodes.Count == 0 ? ["material.shortage"] : readiness.ReasonCodes;
            risks.AddRange(reasons.Select(reason => new ExecutionRiskFact(
                reason, ExecutionRiskCategory.Material, !readiness.MaterialReadyUtc.HasValue,
                readiness.ScopeId, calculatedAtUtc)));
        }

        foreach (var window in problem.UnavailabilityWindows.Where(x =>
                     (x.ResourceId is not null && resourceIds.Contains(x.ResourceId)) ||
                     (x.WorkCenterId is not null && workCenterIds.Contains(x.WorkCenterId))))
        {
            risks.Add(new ExecutionRiskFact(
                string.IsNullOrWhiteSpace(window.ReasonCode) ? "equipment.unavailable" : window.ReasonCode,
                ExecutionRiskCategory.Equipment,
                window.EndUtc > calculatedAtUtc,
                window.ResourceId ?? window.WorkCenterId ?? "equipment",
                calculatedAtUtc));
        }

        foreach (var block in problem.QualityBlocks.Where(x => Applies(x.ScopeType, x.ScopeId, orderId, operationIds, resourceIds, workCenterIds)))
        {
            risks.Add(new ExecutionRiskFact(
                string.IsNullOrWhiteSpace(block.ReasonCode) ? "quality.hold" : block.ReasonCode,
                ExecutionRiskCategory.Quality,
                !block.BlockedUntilUtc.HasValue || block.BlockedUntilUtc > calculatedAtUtc,
                block.ScopeId,
                calculatedAtUtc));
        }

        foreach (var operation in order.Operations.Where(x => !x.ToolingAvailable))
        {
            risks.Add(new ExecutionRiskFact(
                "tooling.unavailable", ExecutionRiskCategory.Tooling, true, operation.OperationId, calculatedAtUtc));
        }

        foreach (var conflict in plan.Conflicts.Where(x => string.Equals(x.OrderId, orderId, StringComparison.Ordinal)))
        {
            var mapped = MapConflict(conflict.ReasonCode);
            if (mapped is not null)
            {
                risks.Add(new ExecutionRiskFact(
                    mapped.Value.ReasonCode,
                    mapped.Value.Category,
                    conflict.Severity == ScheduleConflictSeverityContract.Error,
                    conflict.OperationId ?? conflict.ConflictId,
                    plan.GeneratedAtUtc));
            }
        }

        foreach (var unscheduled in plan.UnscheduledOperations.Where(x => string.Equals(x.OrderId, orderId, StringComparison.Ordinal)))
        {
            var mapped = MapConflict(unscheduled.ReasonCode);
            if (mapped is not null)
            {
                risks.Add(new ExecutionRiskFact(
                    mapped.Value.ReasonCode,
                    mapped.Value.Category,
                    true,
                    unscheduled.OperationId,
                    plan.GeneratedAtUtc));
            }
        }

        var assignmentEnds = plan.Assignments
            .Where(x => string.Equals(x.OrderId, orderId, StringComparison.Ordinal))
            .Select(x => x.EndUtc)
            .ToArray();
        var remainingCycle = assignmentEnds.Length > 0
            ? Max(TimeSpan.Zero, assignmentEnds.Max() - calculatedAtUtc)
            : TimeSpan.FromMinutes(order.Operations.Sum(x => Math.Max(0, x.DurationMinutes + x.SetupMinutes)));

        return OrderUrgencyCalculator.Calculate(new OrderUrgencyCalculationInput(
            order.OrderId,
            string.IsNullOrWhiteSpace(order.BusinessReference) ? order.OrderId : order.BusinessReference,
            calculatedAtUtc,
            order.DueUtc,
            remainingCycle,
            businessPriority,
            risks,
            false,
            isSourceStale,
            plan.GeneratedAtUtc,
            inputFingerprint));
    }

    private static bool Applies(
        string scopeType,
        string scopeId,
        string orderId,
        IReadOnlySet<string> operationIds,
        IReadOnlySet<string> resourceIds,
        IReadOnlySet<string> workCenterIds) => scopeType.ToLowerInvariant() switch
    {
        "order" or "workorder" or "work-order" => string.Equals(scopeId, orderId, StringComparison.Ordinal),
        "operation" => operationIds.Contains(scopeId),
        "resource" => resourceIds.Contains(scopeId),
        "workcenter" or "work-center" => workCenterIds.Contains(scopeId),
        "global" => true,
        _ => false,
    };

    private static (string ReasonCode, ExecutionRiskCategory Category)? MapConflict(ScheduleConflictReasonCodeContract reason) => reason switch
    {
        ScheduleConflictReasonCodeContract.Material => ("material.shortage", ExecutionRiskCategory.Material),
        ScheduleConflictReasonCodeContract.Equipment => ("equipment.unavailable", ExecutionRiskCategory.Equipment),
        ScheduleConflictReasonCodeContract.Quality => ("quality.hold", ExecutionRiskCategory.Quality),
        ScheduleConflictReasonCodeContract.Tooling => ("tooling.unavailable", ExecutionRiskCategory.Tooling),
        ScheduleConflictReasonCodeContract.Capacity or ScheduleConflictReasonCodeContract.Calendar or
            ScheduleConflictReasonCodeContract.NoEligibleResource or ScheduleConflictReasonCodeContract.OutsideHorizon
            => ("capacity.insufficient", ExecutionRiskCategory.Capacity),
        _ => null,
    };

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
