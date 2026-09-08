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
        string? operationCode,
        string? requiredSkillCode)
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
        RequiredSkillCode = NormalizeOptional(requiredSkillCode);
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
    public string? MachineTimeExecutionDeviceAssetId { get; private set; }
    public bool MachineTimeEvidenceUnavailable { get; private set; } = true;
    public long ActualTimeSettlementRevision { get; private set; }
    public RowVersion RowVersion { get; private set; } = new(0);
    public string? AssignedUserId { get; private set; }

    /// <summary>Display name of the assigned worker captured when the task was dispatched.</summary>
    public string? AssignedUserName { get; private set; }
    public string? DeviceAssetId { get; private set; }

    /// <summary>
    /// MasterData shift public id (e.g. EARLY / MIDDLE). This is the *shift* dimension — the working
    /// window — and must never carry a team code: a shift and a team are distinct MasterData aggregates
    /// and a team already references the shift it works.
    /// </summary>
    public string? ShiftId { get; private set; }

    /// <summary>MasterData team public id (e.g. TEAM-WB-MC-A) captured by MES dispatch.</summary>
    public string? TeamId { get; private set; }

    /// <summary>
    /// Display name of the assigned team captured when the task was dispatched. Snapshot, like
    /// <see cref="AssignedUserName"/>: a dispatch record states who it was at the time, and MES must not
    /// read MasterData to render its own read face.
    /// </summary>
    public string? TeamName { get; private set; }
    public DateTimeOffset? AssignedAtUtc { get; private set; }
    public long ManualDispatchRevision { get; private set; }
    public bool HasActiveManualDispatch { get; private set; }
    // Set only when a released APS schedule places this task (ApplyScheduleAssignment); never by manual
    // dispatch (Assign). This is the schedule-specific fact that distinguishes 已排程 from 未排程.
    public DateTimeOffset? ScheduledAtUtc { get; private set; }
    public string? SchedulePlanId { get; private set; }
    public long? ScheduleReleaseRevision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = "pcs";
    public decimal PlannedQuantity { get; private set; }
    public bool RequiresQualityInspection { get; private set; }
    public string? OperationCode { get; private set; }
    public string? RequiredSkillCode { get; private set; }
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
        string? operationCode = null,
        string? requiredSkillCode = null)
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
            operationCode,
            requiredSkillCode);
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
        string? operationCode = null,
        string? requiredSkillCode = null)
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
            operationCode,
            requiredSkillCode);
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
        StartMachineTimeExecutionWindow();
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

    public void Complete(
        DateTimeOffset completedAtUtc,
        IReadOnlyCollection<string> coveredProductionReportNos)
    {
        ArgumentNullException.ThrowIfNull(coveredProductionReportNos);
        FreezeCompletion(completedAtUtc);

        ActualTimeSettlementRevision = checked(ActualTimeSettlementRevision + 1);
        var normalizedReportNos = NormalizeCoveredProductionReportNos(coveredProductionReportNos);
        AddDomainEvent(new OperationTaskCompletedDomainEvent(this));
        AddDomainEvent(new OperationActualTimeSettledDomainEvent(CreateActualTimeSettlementSnapshot(normalizedReportNos)));
    }

    /// <summary>
    /// Imports a completed historical task without inventing a settlement revision or integration fact.
    /// Runtime completion must use the governed settlement coordinator instead.
    /// </summary>
    internal void CompleteLegacyHistoryWithoutSettlement(DateTimeOffset completedAtUtc)
    {
        if (ActualTimeSettlementRevision != 0)
        {
            throw new InvalidOperationException("Settled operation task cannot be imported as legacy history.");
        }

        FreezeCompletion(completedAtUtc);
        AddDomainEvent(new OperationTaskCompletedDomainEvent(this));
    }

    private void FreezeCompletion(DateTimeOffset completedAtUtc)
    {
        if (Status != OperationTaskLifecycleStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress operation task can be completed.");
        }

        if (ExistingStartUtc is { } existingStartUtc && completedAtUtc < existingStartUtc)
        {
            throw new InvalidOperationException("Operation task cannot be completed before its current start time.");
        }

        Status = OperationTaskLifecycleStatus.Completed;
        ExistingStartUtc ??= completedAtUtc;
        ExistingEndUtc = completedAtUtc;
        var elapsedTicks = Math.Max(0L, (completedAtUtc - ExistingStartUtc.Value).Ticks - PausedDurationTicks);
        LaborTimeTicks = elapsedTicks;
        MachineTimeTicks = elapsedTicks;
    }

    public void ReopenAfterReportReversal(
        OperationActualTimeSettlementSnapshot settlement,
        DateTimeOffset voidedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (Status != OperationTaskLifecycleStatus.Completed)
        {
            return;
        }

        if (ActualTimeSettlementRevision <= 0 ||
            settlement.SettlementRevision != ActualTimeSettlementRevision ||
            !string.Equals(settlement.OperationTaskId, OperationTaskIdValue, StringComparison.Ordinal) ||
            !string.Equals(settlement.OrganizationId, OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(settlement.EnvironmentId, EnvironmentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Completed operation task has no matching active actual-time settlement.");
        }

        if (voidedAtUtc < settlement.CompletedAtUtc)
        {
            throw new InvalidOperationException("Actual-time settlement cannot be voided before its completion time.");
        }

        Status = OperationTaskLifecycleStatus.InProgress;
        ExistingStartUtc = voidedAtUtc;
        ExistingEndUtc = null;
        PausedAtUtc = null;
        PausedDurationTicks = 0;
        LaborTimeTicks = 0;
        MachineTimeTicks = 0;
        StartMachineTimeExecutionWindow();
        AddDomainEvent(new OperationActualTimeSettlementVoidedDomainEvent(settlement, voidedAtUtc));
    }

    private OperationActualTimeSettlementSnapshot CreateActualTimeSettlementSnapshot(
        IReadOnlyCollection<string> coveredProductionReportNos) =>
        new(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            OperationTaskIdValue,
            WorkCenterId,
            ActualTimeSettlementRevision,
            ExistingEndUtc ?? throw new InvalidOperationException("Completed operation task must have an end time."),
            LaborTimeTicks,
            MachineTimeTicks,
            coveredProductionReportNos.ToArray(),
            MachineTimeEvidenceUnavailable ? null : MachineTimeExecutionDeviceAssetId,
            MachineTimeEvidenceUnavailable
                ? MachineTimeFactStatus.Unavailable
                : MachineTimeFactStatus.Available,
            MachineTimeEvidenceUnavailable ? null : MachineTimeTicks,
            MachineTimeEvidenceUnavailable
                ? null
                : MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1);

    private static string[] NormalizeCoveredProductionReportNos(
        IReadOnlyCollection<string> coveredProductionReportNos) =>
        coveredProductionReportNos
            .Select(x => DomainGuard.Required(x, nameof(coveredProductionReportNos)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public void Assign(
        string? assignedUserId,
        string? deviceAssetId,
        string? shiftId,
        DateTimeOffset assignedAtUtc,
        string actor = "system:mes",
        string? assignedUserName = null,
        string? teamId = null,
        string? teamName = null)
    {
        if (Status is OperationTaskLifecycleStatus.Completed or OperationTaskLifecycleStatus.Cancelled)
        {
            throw new InvalidOperationException("Closed operation task cannot be assigned.");
        }

        if (Status == OperationTaskLifecycleStatus.ScheduleInvalidated)
        {
            throw new KnownException("排程已失效的工序任务必须重新排程后才能派工。");
        }

        var previousDeviceAssetId = HasActiveManualDispatch || HasLegacyUnknownManualDispatch
            ? DeviceAssetId
            : null;
        var previousAssignedAtUtc = AssignedAtUtc;
        var normalizedDeviceAssetId = NormalizeOptional(deviceAssetId);
        if (Status is OperationTaskLifecycleStatus.InProgress or OperationTaskLifecycleStatus.Paused &&
            !string.Equals(MachineTimeExecutionDeviceAssetId, normalizedDeviceAssetId, StringComparison.Ordinal))
        {
            MachineTimeEvidenceUnavailable = true;
        }
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
        AssignedUserName = AssignedUserId is null ? null : NormalizeOptional(assignedUserName);
        DeviceAssetId = normalizedDeviceAssetId;
        ShiftId = NormalizeOptional(shiftId);
        TeamId = NormalizeOptional(teamId);
        TeamName = TeamId is null ? null : NormalizeOptional(teamName);
        AssignedAtUtc = assignedAtUtc;
        HasActiveManualDispatch = isManualDispatch;

        if (isManualDispatch)
        {
            AddDomainEvent(new OperationTaskManuallyDispatchedDomainEvent(
                CreateManualDispatchSnapshot(normalizedDeviceAssetId!, assignedAtUtc, ManualDispatchRevision),
                canonicalActor!));
        }
    }

    public void Claim(
        string assignedUserId,
        string assignedUserName,
        string? deviceAssetId,
        string? shiftId,
        DateTimeOffset assignedAtUtc,
        string actor,
        string? teamId = null,
        string? teamName = null)
    {
        if (Status != OperationTaskLifecycleStatus.Queued)
        {
            throw new KnownException("只有待领取的工序任务可以领取。");
        }

        if (AssignedUserId is not null)
        {
            throw new KnownException("该工序任务已被领取。");
        }

        Assign(
            assignedUserId,
            deviceAssetId,
            shiftId,
            assignedAtUtc,
            actor,
            assignedUserName,
            teamId,
            teamName);
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
        string? operationCode = null,
        string? schedulePlanId = null,
        long? scheduleReleaseRevision = null)
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

        var normalizedSchedulePlanId = NormalizeOptional(schedulePlanId);
        if (normalizedSchedulePlanId is null && scheduleReleaseRevision is not null)
        {
            throw new ArgumentException("Schedule plan id is required when a release revision is supplied.");
        }

        if (scheduleReleaseRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scheduleReleaseRevision), "Schedule release revision must be positive.");
        }

        if (ScheduleReleaseRevision is not null && scheduleReleaseRevision is not null &&
            ScheduleReleaseRevision.Value > scheduleReleaseRevision.Value)
        {
            return;
        }

        if (ScheduleReleaseRevision is not null && scheduleReleaseRevision is null)
        {
            return;
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
        SchedulePlanId = normalizedSchedulePlanId;
        ScheduleReleaseRevision = scheduleReleaseRevision;
        if (Status == OperationTaskLifecycleStatus.ScheduleInvalidated)
        {
            Status = OperationTaskLifecycleStatus.Queued;
        }

        // A released schedule assignment re-plans the task, so any prior invalidation reason no longer applies.
        ScheduleInvalidationReasonCode = null;
    }

    public void RevokeScheduleAssignment(string schedulePlanId, long scheduleReleaseRevision, string reasonCode)
    {
        var normalizedPlanId = DomainGuard.Required(schedulePlanId, nameof(schedulePlanId));
        if (scheduleReleaseRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scheduleReleaseRevision), "Schedule release revision must be positive.");
        }

        if (!string.Equals(SchedulePlanId, normalizedPlanId, StringComparison.Ordinal) ||
            (ScheduleReleaseRevision is not null && ScheduleReleaseRevision.Value != scheduleReleaseRevision))
        {
            return;
        }

        SchedulePlanId = null;
        ScheduleReleaseRevision = null;
        ScheduledAtUtc = null;

        if (!HasActiveManualDispatch &&
            Status is not (OperationTaskLifecycleStatus.InProgress or
                OperationTaskLifecycleStatus.Paused or
                OperationTaskLifecycleStatus.Completed or
                OperationTaskLifecycleStatus.Cancelled))
        {
            DeviceAssetId = null;
            AssignedAtUtc = null;
        }

        MarkScheduleInvalidated(reasonCode);
    }

    public void ReconcileLegacyScheduleAssignment(string reasonCode)
    {
        if (SchedulePlanId is not null || ScheduleReleaseRevision is not null || ScheduledAtUtc is null)
        {
            return;
        }

        ScheduledAtUtc = null;
        if (!HasActiveManualDispatch &&
            Status is not (OperationTaskLifecycleStatus.InProgress or
                OperationTaskLifecycleStatus.Paused or
                OperationTaskLifecycleStatus.Completed or
                OperationTaskLifecycleStatus.Cancelled))
        {
            DeviceAssetId = null;
            AssignedAtUtc = null;
        }

        MarkScheduleInvalidated(reasonCode);
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

    private void StartMachineTimeExecutionWindow()
    {
        MachineTimeExecutionDeviceAssetId = NormalizeOptional(DeviceAssetId);
        MachineTimeEvidenceUnavailable = MachineTimeExecutionDeviceAssetId is null;
    }

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
