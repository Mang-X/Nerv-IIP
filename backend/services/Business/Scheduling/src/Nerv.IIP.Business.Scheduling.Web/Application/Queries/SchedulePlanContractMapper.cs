using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public static class SchedulePlanContractMapper
{
    public static SchedulePlanContract ToContract(SchedulePlan plan)
    {
        var status = ToContractStatus(plan.Status);
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
                x.ExplanationCode,
                x.StandardOperationCode))
            .ToArray();
        var conflicts = plan.Conflicts
            .OrderBy(x => x.ConflictPublicId, StringComparer.Ordinal)
            .Select(x => new ScheduleConflictContract(
                x.ConflictPublicId,
                ToContractReasonCode(x.ReasonCode),
                ToContractSeverity(x.Severity),
                string.IsNullOrWhiteSpace(x.WorkOrderId) ? null : x.WorkOrderId,
                string.IsNullOrWhiteSpace(x.OperationId) ? null : x.OperationId,
                string.IsNullOrWhiteSpace(x.ResourceId) ? null : x.ResourceId,
                x.Message))
            .ToArray();
        var conflictByOperation = conflicts
            .Where(x => x.OperationId is not null)
            .GroupBy(x => (x.OrderId ?? string.Empty, x.OperationId!))
            .ToDictionary(x => x.Key, x => x.First());
        var changeSummary = assignments
            .Select(x => ToChangeSummary(x, conflictByOperation))
            .Concat(plan.UnscheduledOperations
                .OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .Select(x => new ScheduleChangeContract(
                    x.WorkOrderId,
                    x.OperationId,
                    ScheduleChangeTypeContract.Blocked,
                    x.Message)))
            .ToArray();

        return new SchedulePlanContract(
            ContractVersion: plan.ContractVersion,
            PlanId: plan.PlanId,
            ProblemId: plan.ProblemId,
            ProblemFingerprint: plan.ProblemFingerprint,
            AlgorithmVersion: plan.AlgorithmVersion,
            Status: status,
            GeneratedAtUtc: plan.GeneratedAtUtc,
            Metrics: new SchedulePlanMetricsContract(
                plan.ScheduledOperationCount,
                plan.UnscheduledOperationCount,
                plan.AssignedMinutes,
                plan.MakespanMinutes,
                plan.TotalTardinessMinutes,
                plan.LateOperationCount,
                plan.OnTimeRate,
                plan.AverageResourceUtilization),
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
                    ToContractReasonCode(x.ReasonCode),
                    x.Message))
                .ToArray(),
            ChangeSummary: changeSummary,
            GanttItems: assignments.Select(x =>
            {
                var key = (x.OrderId, x.OperationId);
                var hasConflict = conflictByOperation.TryGetValue(key, out var conflict);
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
                    HasConflict: hasConflict,
                    ConflictReasonCode: hasConflict ? conflict!.ReasonCode : null);
            }).ToArray());
    }

    private static ScheduleChangeContract ToChangeSummary(
        ScheduleAssignmentContract assignment,
        IReadOnlyDictionary<(string OrderId, string OperationId), ScheduleConflictContract> conflictByOperation)
    {
        var key = (assignment.OrderId, assignment.OperationId);
        if (assignment.IsLocked)
        {
            return new ScheduleChangeContract(
                assignment.OrderId,
                assignment.OperationId,
                ScheduleChangeTypeContract.Preserved,
                "Locked assignment reserved before APS lite scheduling.");
        }

        if (conflictByOperation.TryGetValue(key, out var conflict)
            && conflict.ReasonCode == ScheduleConflictReasonCodeContract.DueDate)
        {
            return new ScheduleChangeContract(
                assignment.OrderId,
                assignment.OperationId,
                ScheduleChangeTypeContract.Delayed,
                conflict.Message);
        }

        return new ScheduleChangeContract(
            assignment.OrderId,
            assignment.OperationId,
            ScheduleChangeTypeContract.Added,
            "Scheduled by APS lite.");
    }

    public static SchedulePlanStatusContract ToContractStatus(SchedulePlanLifecycleStatus status)
    {
        return status switch
        {
            SchedulePlanLifecycleStatus.Generated => SchedulePlanStatusContract.Generated,
            SchedulePlanLifecycleStatus.Released => SchedulePlanStatusContract.Released,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported schedule plan lifecycle status.")
        };
    }

    public static SchedulePlanContract WithStatus(SchedulePlanContract plan, SchedulePlanStatusContract status)
    {
        return plan with
        {
            Status = status,
            GanttItems = plan.GanttItems.Select(x => x with { Status = status }).ToArray()
        };
    }

    public static GeneratedSchedulePlanSnapshot ToDomainSnapshot(SchedulePlanContract plan)
    {
        return new GeneratedSchedulePlanSnapshot(
            plan.PlanId,
            plan.ProblemId,
            plan.ProblemFingerprint,
            plan.AlgorithmVersion,
            plan.ContractVersion,
            plan.GeneratedAtUtc,
            ToDomainStatus(plan.Status),
            new GeneratedSchedulePlanMetricsSnapshot(
                plan.Metrics.ScheduledOperationCount,
                plan.Metrics.UnscheduledOperationCount,
                plan.Metrics.AssignedMinutes,
                plan.Metrics.MakespanMinutes,
                plan.Metrics.TotalTardinessMinutes,
                plan.Metrics.LateOperationCount,
                plan.Metrics.OnTimeRate,
                plan.Metrics.AverageResourceUtilization),
            plan.Assignments
                .Select(x => new GeneratedScheduleAssignmentSnapshot(
                    x.AssignmentId,
                    x.OrderId,
                    x.OperationId,
                    x.OperationSequence,
                    x.ResourceId,
                    x.WorkCenterId,
                    x.StartUtc,
                    x.EndUtc,
                    x.IsLocked,
                    x.ExplanationCode,
                    x.StandardOperationCode))
                .ToArray(),
            plan.ResourceLoads
                .Select(x => new GeneratedScheduleResourceLoadSnapshot(
                    x.ResourceId,
                    x.WindowStartUtc,
                    x.WindowEndUtc,
                    x.AssignedMinutes,
                    x.AvailableMinutes,
                    x.Utilization))
                .ToArray(),
            plan.Conflicts
                .Select(x => new GeneratedScheduleConflictSnapshot(
                    x.ConflictId,
                    ToDomainReasonCode(x.ReasonCode),
                    ToDomainSeverity(x.Severity),
                    x.OrderId,
                    x.OperationId,
                    x.ResourceId,
                    x.Message))
                .ToArray(),
            plan.UnscheduledOperations
                .Select(x => new GeneratedUnscheduledOperationSnapshot(
                    x.OrderId,
                    x.OperationId,
                    ToDomainReasonCode(x.ReasonCode),
                    x.Message))
                .ToArray());
    }

    public static ScheduleConflictReasonCodeContract ToContractReasonCode(ScheduleConflictReasonCode reasonCode)
    {
        return reasonCode switch
        {
            ScheduleConflictReasonCode.Capacity => ScheduleConflictReasonCodeContract.Capacity,
            ScheduleConflictReasonCode.Calendar => ScheduleConflictReasonCodeContract.Calendar,
            ScheduleConflictReasonCode.Equipment => ScheduleConflictReasonCodeContract.Equipment,
            ScheduleConflictReasonCode.Material => ScheduleConflictReasonCodeContract.Material,
            ScheduleConflictReasonCode.Quality => ScheduleConflictReasonCodeContract.Quality,
            ScheduleConflictReasonCode.DueDate => ScheduleConflictReasonCodeContract.DueDate,
            ScheduleConflictReasonCode.NoEligibleResource => ScheduleConflictReasonCodeContract.NoEligibleResource,
            ScheduleConflictReasonCode.OutsideHorizon => ScheduleConflictReasonCodeContract.OutsideHorizon,
            ScheduleConflictReasonCode.PredecessorUnscheduled => ScheduleConflictReasonCodeContract.PredecessorUnscheduled,
            ScheduleConflictReasonCode.Tooling => ScheduleConflictReasonCodeContract.Tooling,
            ScheduleConflictReasonCode.InvalidLockedAssignment => ScheduleConflictReasonCodeContract.InvalidLockedAssignment,
            _ => throw new ArgumentOutOfRangeException(nameof(reasonCode), reasonCode, "Unsupported schedule conflict reason code.")
        };
    }

    public static ScheduleConflictSeverityContract ToContractSeverity(ScheduleConflictSeverity severity)
    {
        return severity switch
        {
            ScheduleConflictSeverity.Warning => ScheduleConflictSeverityContract.Warning,
            ScheduleConflictSeverity.Error => ScheduleConflictSeverityContract.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported schedule conflict severity.")
        };
    }

    private static SchedulePlanInputStatus ToDomainStatus(SchedulePlanStatusContract status)
    {
        return status switch
        {
            SchedulePlanStatusContract.Preview => SchedulePlanInputStatus.Preview,
            SchedulePlanStatusContract.Generated => SchedulePlanInputStatus.Generated,
            SchedulePlanStatusContract.Released => SchedulePlanInputStatus.Released,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported schedule plan contract status.")
        };
    }

    private static ScheduleConflictReasonCode ToDomainReasonCode(ScheduleConflictReasonCodeContract reasonCode)
    {
        return reasonCode switch
        {
            ScheduleConflictReasonCodeContract.Capacity => ScheduleConflictReasonCode.Capacity,
            ScheduleConflictReasonCodeContract.Calendar => ScheduleConflictReasonCode.Calendar,
            ScheduleConflictReasonCodeContract.Equipment => ScheduleConflictReasonCode.Equipment,
            ScheduleConflictReasonCodeContract.Material => ScheduleConflictReasonCode.Material,
            ScheduleConflictReasonCodeContract.Quality => ScheduleConflictReasonCode.Quality,
            ScheduleConflictReasonCodeContract.DueDate => ScheduleConflictReasonCode.DueDate,
            ScheduleConflictReasonCodeContract.NoEligibleResource => ScheduleConflictReasonCode.NoEligibleResource,
            ScheduleConflictReasonCodeContract.OutsideHorizon => ScheduleConflictReasonCode.OutsideHorizon,
            ScheduleConflictReasonCodeContract.PredecessorUnscheduled => ScheduleConflictReasonCode.PredecessorUnscheduled,
            ScheduleConflictReasonCodeContract.InvalidLockedAssignment => ScheduleConflictReasonCode.InvalidLockedAssignment,
            ScheduleConflictReasonCodeContract.Tooling => ScheduleConflictReasonCode.Tooling,
            _ => throw new ArgumentOutOfRangeException(nameof(reasonCode), reasonCode, "Unsupported schedule conflict reason code.")
        };
    }

    private static ScheduleConflictSeverity ToDomainSeverity(ScheduleConflictSeverityContract severity)
    {
        return severity switch
        {
            ScheduleConflictSeverityContract.Warning => ScheduleConflictSeverity.Warning,
            ScheduleConflictSeverityContract.Error => ScheduleConflictSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported schedule conflict severity.")
        };
    }
}
