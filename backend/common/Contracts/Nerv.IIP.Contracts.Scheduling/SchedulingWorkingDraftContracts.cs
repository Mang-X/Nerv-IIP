namespace Nerv.IIP.Contracts.Scheduling;

/// <summary>Version 1 stores editing state only. Restore the baseline from PlanId; undo/redo and derived graph data are not persisted.</summary>
public sealed record SchedulingWorkingDraftStateContract(
    int ContractVersion,
    IReadOnlyList<SchedulingWorkingDraftOrderContract> Orders,
    IReadOnlyList<SchedulingWorkingDraftTaskContract> Tasks,
    IReadOnlyList<SchedulingWorkingDraftPendingOperationContract> PendingOperations);

public sealed record SchedulingWorkingDraftOrderContract(string WorkOrderId, int Priority, bool IsRush, bool Included);

/// <summary>Operation task state; task id matches the baseline graph id. Segments preserve interruptible operation edits.</summary>
public sealed record SchedulingWorkingDraftTaskContract(
    string TaskId, string OrderId, string OperationId, string ResourceId, string WorkCenterId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool Locked,
    IReadOnlyList<ScheduleAssignmentSegmentContract>? Segments = null);

public enum SchedulingWorkingDraftPendingSource { Unscheduled, Invalidated, Removed }

public sealed record SchedulingWorkingDraftPendingOperationContract(
    string Id, string OrderId, string OperationId, SchedulingWorkingDraftPendingSource Source, string Message, bool CanRestore,
    string? TaskId = null, string? ReasonCode = null, SchedulingWorkingDraftTaskContract? Task = null);

public sealed record SchedulingWorkingDraftContract(
    string PlanId, DateTimeOffset SavedAtUtc, SchedulingWorkingDraftStateContract State);

public static class SchedulingWorkingDraftHeaders
{
    // Only an authenticated internal caller may forward this value. Gateways must derive it from their authenticated user.
    public const string UserId = "X-Scheduling-User-Id";
}
