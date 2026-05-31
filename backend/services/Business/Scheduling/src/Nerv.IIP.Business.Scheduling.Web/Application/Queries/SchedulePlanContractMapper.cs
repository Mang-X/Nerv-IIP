using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

internal static class SchedulePlanContractMapper
{
    public static SchedulePlanContract ToContract(SchedulePlan plan)
    {
        var status = plan.Status == SchedulePlanLifecycleStatus.Released
            ? SchedulePlanStatusContract.Released
            : SchedulePlanStatusContract.Generated;
        var assignments = plan.Assignments
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .Select(x => new ScheduleAssignmentContract(
                x.AssignmentId,
                x.WorkOrderId,
                x.OperationId,
                x.OperationSequence,
                x.ResourceId,
                x.WorkCenterId,
                x.StartUtc,
                x.EndUtc,
                x.IsLocked,
                x.ExplanationCode))
            .ToArray();
        var conflicts = plan.Conflicts
            .OrderBy(x => x.ConflictPublicId, StringComparer.Ordinal)
            .Select(x => new ScheduleConflictContract(
                x.ConflictPublicId,
                x.ReasonCode,
                x.Severity,
                string.IsNullOrWhiteSpace(x.WorkOrderId) ? null : x.WorkOrderId,
                string.IsNullOrWhiteSpace(x.OperationId) ? null : x.OperationId,
                string.IsNullOrWhiteSpace(x.ResourceId) ? null : x.ResourceId,
                x.Message))
            .ToArray();
        var conflictByOperation = conflicts
            .Where(x => x.OperationId is not null)
            .GroupBy(x => (x.OrderId ?? string.Empty, x.OperationId!))
            .ToDictionary(x => x.Key, x => x.First().ReasonCode);

        return new SchedulePlanContract(
            ContractVersion: plan.ContractVersion,
            PlanId: plan.PlanId,
            ProblemId: plan.ProblemId,
            ProblemFingerprint: plan.ProblemFingerprint,
            AlgorithmVersion: plan.AlgorithmVersion,
            Status: status,
            GeneratedAtUtc: plan.GeneratedAtUtc,
            Assignments: assignments,
            ResourceLoads: plan.ResourceLoads
                .OrderBy(x => x.WindowStartUtc)
                .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                .Select(x => new ScheduleResourceLoadContract(
                    x.ResourceId,
                    x.WindowStartUtc,
                    x.WindowEndUtc,
                    x.AssignedMinutes,
                    x.AvailableMinutes,
                    x.Utilization))
                .ToArray(),
            Conflicts: conflicts,
            UnscheduledOperations: plan.UnscheduledOperations
                .OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .Select(x => new UnscheduledOperationContract(
                    x.WorkOrderId,
                    x.OperationId,
                    x.ReasonCode,
                    x.Message))
                .ToArray(),
            ChangeSummary: [],
            GanttItems: assignments.Select(x =>
            {
                var key = (x.OrderId, x.OperationId);
                return new GanttScheduleItemContract(
                    ItemId: $"gantt-{x.AssignmentId}",
                    OrderId: x.OrderId,
                    OperationId: x.OperationId,
                    OperationSequence: x.OperationSequence,
                    ResourceId: x.ResourceId,
                    WorkCenterId: x.WorkCenterId,
                    StartUtc: x.StartUtc,
                    EndUtc: x.EndUtc,
                    Status: status,
                    HasConflict: conflictByOperation.ContainsKey(key),
                    ConflictReasonCode: conflictByOperation.GetValueOrDefault(key));
            }).ToArray());
    }

    public static SchedulePlanContract WithStatus(SchedulePlanContract plan, SchedulePlanStatusContract status)
    {
        return plan with
        {
            Status = status,
            GanttItems = plan.GanttItems.Select(x => x with { Status = status }).ToArray()
        };
    }
}
