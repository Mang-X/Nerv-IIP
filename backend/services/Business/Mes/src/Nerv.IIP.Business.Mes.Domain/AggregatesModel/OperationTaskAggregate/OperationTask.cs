using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

public partial record OperationTaskId : IGuidStronglyTypedId;

public enum OperationTaskLifecycleStatus
{
    Queued,
    InProgress,
    Paused,
    ScheduleInvalidated,
    Completed,
    Cancelled,
}

public sealed class OperationTask : Entity<OperationTaskId>, IAggregateRoot
{
    private OperationTask()
    {
    }

    private OperationTask(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        OperationTaskLifecycleStatus status,
        int operationSequence,
        string workCenterId,
        IReadOnlyCollection<string> alternativeWorkCenterIds,
        DateTimeOffset earliestStartUtc,
        TimeSpan duration,
        DateTimeOffset? existingStartUtc,
        DateTimeOffset? existingEndUtc,
        string? skuCode,
        string? uomCode,
        decimal plannedQuantity,
        bool requiresQualityInspection,
        string? operationCode)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskIdValue = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        Status = status;
        OperationSequence = operationSequence;
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
        AlternativeWorkCenterIds = NormalizeAlternatives(alternativeWorkCenterIds);
        EarliestStartUtc = earliestStartUtc;
        DurationTicks = duration > TimeSpan.Zero
            ? duration.Ticks
            : throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        ExistingStartUtc = existingStartUtc;
        ExistingEndUtc = existingEndUtc;
        SkuCode = NormalizeOptional(skuCode) ?? workOrderId;
        UomCode = NormalizeOptional(uomCode) ?? "pcs";
        PlannedQuantity = plannedQuantity > 0m ? plannedQuantity : 1m;
        RequiresQualityInspection = requiresQualityInspection;
        OperationCode = NormalizeOptional(operationCode);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskIdValue { get; private set; } = string.Empty;
    public OperationTaskLifecycleStatus Status { get; private set; }
    public int OperationSequence { get; private set; }
    public string WorkCenterId { get; private set; } = string.Empty;
    public string AlternativeWorkCenterIds { get; private set; } = string.Empty;
    public DateTimeOffset EarliestStartUtc { get; private set; }
    public long DurationTicks { get; private set; }
    public DateTimeOffset? ExistingStartUtc { get; private set; }
    public DateTimeOffset? ExistingEndUtc { get; private set; }
    public DateTimeOffset? PausedAtUtc { get; private set; }
    public long PausedDurationTicks { get; private set; }
    public long LaborTimeTicks { get; private set; }
    public long MachineTimeTicks { get; private set; }
    public string? AssignedUserId { get; private set; }
    public string? DeviceAssetId { get; private set; }
    public string? ShiftId { get; private set; }
    public DateTimeOffset? AssignedAtUtc { get; private set; }
    public long ManualDispatchRevision { get; private set; }
    public bool HasActiveManualDispatch { get; private set; }
    // Set only when a released APS schedule places this task (ApplyScheduleAssignment); never by manual
    // dispatch (Assign). This is the schedule-specific fact that distinguishes 已排程 from 未排程.
    public DateTimeOffset? ScheduledAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = "pcs";
    public decimal PlannedQuantity { get; private set; }
    public bool RequiresQualityInspection { get; private set; }
    public string? OperationCode { get; private set; }
    public string? ScheduleInvalidationReasonCode { get; private set; }

    public string OperationTaskId => OperationTaskIdValue;

    public TimeSpan Duration => TimeSpan.FromTicks(DurationTicks);

    public TimeSpan PausedDuration => TimeSpan.FromTicks(PausedDurationTicks);

    public TimeSpan LaborTime => TimeSpan.FromTicks(LaborTimeTicks);

    public TimeSpan MachineTime => TimeSpan.FromTicks(MachineTimeTicks);

    public IReadOnlyCollection<string> AlternativeWorkCenterIdList =>
        string.IsNullOrWhiteSpace(AlternativeWorkCenterIds)
            ? []
            : AlternativeWorkCenterIds.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static OperationTask Queue(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        int operationSequence,
        string workCenterId,
        IReadOnlyCollection<string> alternativeWorkCenterIds,
        DateTimeOffset earliestStartUtc,
        TimeSpan duration,
        string? skuCode = null,
        string? uomCode = null,
        decimal plannedQuantity = 0m,
        bool requiresQualityInspection = false,
        string? operationCode = null)
    {
        return Create(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            OperationTaskLifecycleStatus.Queued,
            operationSequence,
            workCenterId,
            alternativeWorkCenterIds,
            earliestStartUtc,
            duration,
            null,
            null,
            skuCode,
            uomCode,
            plannedQuantity,
            requiresQualityInspection,
            operationCode);
    }

    public static OperationTask Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        OperationTaskLifecycleStatus status,
        int operationSequence,
        string workCenterId,
        IReadOnlyCollection<string> alternativeWorkCenterIds,
        DateTimeOffset earliestStartUtc,
        TimeSpan duration,
        DateTimeOffset? existingStartUtc,
        DateTimeOffset? existingEndUtc,
        string? skuCode = null,
        string? uomCode = null,
        decimal plannedQuantity = 0m,
        bool requiresQualityInspection = false,
        string? operationCode = null)
    {
        return new OperationTask(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            status,
            operationSequence,
            workCenterId,
            alternativeWorkCenterIds,
            earliestStartUtc,
            duration,
            existingStartUtc,
            existingEndUtc,
            skuCode,
            uomCode,
            plannedQuantity,
            requiresQualityInspection,
            operationCode);
    }

    public void Start(DateTimeOffset startedAtUtc)
    {
        if (Status != OperationTaskLifecycleStatus.Queued)
        {
            throw new InvalidOperationException("Only queued operation task can be started.");
        }

        Status = OperationTaskLifecycleStatus.InProgress;
        ExistingStartUtc ??= startedAtUtc;
        ExistingEndUtc = null;
    }

    public void MarkScheduleInvalidated(string? reasonCode = null)
    {
        if (Status is OperationTaskLifecycleStatus.InProgress or
            OperationTaskLifecycleStatus.Paused or
            OperationTaskLifecycleStatus.Completed or
            OperationTaskLifecycleStatus.Cancelled)
        {
            return;
        }

        Status = OperationTaskLifecycleStatus.ScheduleInvalidated;
        ScheduleInvalidationReasonCode = NormalizeOptional(reasonCode);
    }

    public void Pause(DateTimeOffset pausedAtUtc)
    {
        if (Status != OperationTaskLifecycleStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress operation task can be paused.");
        }

        Status = OperationTaskLifecycleStatus.Paused;
        PausedAtUtc = pausedAtUtc;
    }

    public void Resume(DateTimeOffset resumedAtUtc)
    {
        if (Status != OperationTaskLifecycleStatus.Paused)
        {
            throw new InvalidOperationException("Only paused operation task can be resumed.");
        }

        AccumulatePause(resumedAtUtc);
        Status = OperationTaskLifecycleStatus.InProgress;
        ExistingStartUtc ??= resumedAtUtc;
        ExistingEndUtc = null;
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (Status != OperationTaskLifecycleStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress operation task can be completed.");
        }

        Status = OperationTaskLifecycleStatus.Completed;
        ExistingStartUtc ??= completedAtUtc;
        ExistingEndUtc = completedAtUtc;
        var elapsedTicks = Math.Max(0L, (completedAtUtc - ExistingStartUtc.Value).Ticks - PausedDurationTicks);
        LaborTimeTicks = elapsedTicks;
        MachineTimeTicks = elapsedTicks;
        AddDomainEvent(new OperationTaskCompletedDomainEvent(this));
    }

    public void ReopenAfterReportReversal()
    {
        if (Status != OperationTaskLifecycleStatus.Completed)
        {
            return;
        }

        Status = OperationTaskLifecycleStatus.InProgress;
        ExistingEndUtc = null;
        LaborTimeTicks = 0;
        MachineTimeTicks = 0;
    }

    public void Assign(
        string? assignedUserId,
        string? deviceAssetId,
        string? shiftId,
        DateTimeOffset assignedAtUtc,
        string actor = "system:mes")
    {
        if (Status is OperationTaskLifecycleStatus.Completed or OperationTaskLifecycleStatus.Cancelled)
        {
            throw new InvalidOperationException("Closed operation task cannot be assigned.");
        }

        if (Status == OperationTaskLifecycleStatus.ScheduleInvalidated)
        {
            throw new KnownException("Schedule invalidated operation task cannot be dispatched until it is rescheduled.");
        }

        var previousDeviceAssetId = HasActiveManualDispatch || HasLegacyUnknownManualDispatch
            ? DeviceAssetId
            : null;
        var previousAssignedAtUtc = AssignedAtUtc;
        var normalizedDeviceAssetId = NormalizeOptional(deviceAssetId);
        var isManualDispatch = normalizedDeviceAssetId is not null && Duration > TimeSpan.Zero;
        var clearsManualDispatch = previousDeviceAssetId is not null && !isManualDispatch;
        var canonicalActor = isManualDispatch || clearsManualDispatch
            ? RequireCanonicalActor(actor)
            : null;

        if (isManualDispatch)
        {
            ManualDispatchRevision++;
        }
        else if (clearsManualDispatch)
        {
            ManualDispatchRevision++;
            AddDomainEvent(new OperationTaskManualDispatchClearedDomainEvent(
                CreateManualDispatchSnapshot(
                    previousDeviceAssetId!,
                    previousAssignedAtUtc ?? assignedAtUtc,
                    ManualDispatchRevision),
                OperationTaskManualDispatchClearReason.DeviceCleared,
                assignedAtUtc,
                canonicalActor!));
        }

        AssignedUserId = NormalizeOptional(assignedUserId);
        DeviceAssetId = normalizedDeviceAssetId;
        ShiftId = NormalizeOptional(shiftId);
        AssignedAtUtc = assignedAtUtc;
        HasActiveManualDispatch = isManualDispatch;

        if (isManualDispatch)
        {
            AddDomainEvent(new OperationTaskManuallyDispatchedDomainEvent(
                CreateManualDispatchSnapshot(normalizedDeviceAssetId!, assignedAtUtc, ManualDispatchRevision),
                canonicalActor!));
        }
    }

    public void Cancel(DateTimeOffset cancelledAtUtc, string actor = "system:mes")
    {
        if (Status is OperationTaskLifecycleStatus.Completed or OperationTaskLifecycleStatus.Cancelled)
        {
            return;
        }

        var shouldRevokeManualDispatch = HasActiveManualDispatch || HasLegacyUnknownManualDispatch;
        var canonicalActor = shouldRevokeManualDispatch
            ? RequireCanonicalActor(actor)
            : null;

        if (shouldRevokeManualDispatch)
        {
            ManualDispatchRevision++;
            AddDomainEvent(new OperationTaskManualDispatchClearedDomainEvent(
                CreateManualDispatchSnapshot(
                    DeviceAssetId!,
                    AssignedAtUtc ?? cancelledAtUtc,
                    ManualDispatchRevision),
                OperationTaskManualDispatchClearReason.OperationCancelled,
                cancelledAtUtc,
                canonicalActor!));
            HasActiveManualDispatch = false;
        }

        if (Status == OperationTaskLifecycleStatus.Paused)
        {
            AccumulatePause(cancelledAtUtc);
        }

        Status = OperationTaskLifecycleStatus.Cancelled;
        ExistingEndUtc = cancelledAtUtc;
    }

    public void ApplyScheduleAssignment(
        string workCenterId,
        string? deviceAssetId,
        DateTimeOffset plannedStartUtc,
        DateTimeOffset plannedEndUtc,
        DateTimeOffset assignedAtUtc,
        string? operationCode = null)
    {
        if (Status is OperationTaskLifecycleStatus.Completed or OperationTaskLifecycleStatus.Cancelled)
        {
            throw new InvalidOperationException("Closed operation task cannot be scheduled.");
        }

        if (Status is OperationTaskLifecycleStatus.InProgress or OperationTaskLifecycleStatus.Paused)
        {
            throw new KnownException($"Operation task in {Status} cannot be rescheduled by released schedule assignment.");
        }

        if (plannedEndUtc <= plannedStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedEndUtc), "Planned end must be after planned start.");
        }

        var normalizedWorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
        if (!HasActiveManualDispatch)
        {
            WorkCenterId = normalizedWorkCenterId;
            EarliestStartUtc = plannedStartUtc;
            DurationTicks = (plannedEndUtc - plannedStartUtc).Ticks;
            DeviceAssetId = NormalizeOptional(deviceAssetId);
            AssignedAtUtc = assignedAtUtc;
        }

        OperationCode = NormalizeOptional(operationCode) ?? OperationCode;
        ScheduledAtUtc = assignedAtUtc;
        if (Status == OperationTaskLifecycleStatus.ScheduleInvalidated)
        {
            Status = OperationTaskLifecycleStatus.Queued;
        }

        // A released schedule assignment re-plans the task, so any prior invalidation reason no longer applies.
        ScheduleInvalidationReasonCode = null;
    }

    private static string NormalizeAlternatives(IReadOnlyCollection<string> values)
    {
        return string.Join('|', values.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private bool HasLegacyUnknownManualDispatch =>
        ManualDispatchRevision == 0 && !HasActiveManualDispatch && DeviceAssetId is not null;

    private OperationTaskManualDispatchSnapshot CreateManualDispatchSnapshot(
        string resourceId,
        DateTimeOffset occurredAtUtc,
        long dispatchRevision)
    {
        return new OperationTaskManualDispatchSnapshot(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            OperationTaskId,
            OperationSequence,
            resourceId,
            WorkCenterId,
            EarliestStartUtc,
            EarliestStartUtc + Duration,
            occurredAtUtc,
            dispatchRevision);
    }

    private static string RequireCanonicalActor(string actor)
    {
        var normalized = DomainGuard.Required(actor, nameof(actor));
        var separator = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == normalized.Length - 1)
        {
            throw new ArgumentException("A canonical dispatch actor is required.", nameof(actor));
        }

        if (normalized.Length > 128)
        {
            throw new ArgumentException("Dispatch actor cannot exceed 128 characters.", nameof(actor));
        }

        return normalized;
    }

    private void AccumulatePause(DateTimeOffset resumedAtUtc)
    {
        if (PausedAtUtc is null)
        {
            return;
        }

        if (resumedAtUtc > PausedAtUtc.Value)
        {
            PausedDurationTicks += (resumedAtUtc - PausedAtUtc.Value).Ticks;
        }

        PausedAtUtc = null;
    }
}
