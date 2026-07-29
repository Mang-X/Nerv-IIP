namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

public partial record WarehouseTaskId : IGuidStronglyTypedId;

public enum WarehouseTaskType
{
    Putaway = 0,
    Picking = 1,
    Replenishment = 2,
}

public enum WarehouseTaskStatus
{
    Open = 0,
    InProgress = 1,
    Completed = 2,
    CompletedWithDifference = 3,
    Exception = 4,
    Cancelled = 5,
}

public enum WarehouseTaskExecutionChannel
{
    LegacyUnclaimed = 0,
    Unclaimed = 1,
    Manual = 2,
    Wcs = 3,
}

public sealed class WarehouseTask : Entity<WarehouseTaskId>, IAggregateRoot
{
    private WarehouseTask()
    {
    }

    private WarehouseTask(
        WarehouseTaskType taskType,
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string fromLocationCode,
        string toLocationCode,
        decimal plannedQuantity,
        string? lotNo,
        string? serialNo,
        string? assignedOperatorUserId,
        string? assignedPoolCode)
    {
        TaskType = taskType;
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        TaskNo = WmsText.Required(taskNo, nameof(taskNo));
        SourceOrderNo = WmsText.Required(sourceOrderNo, nameof(sourceOrderNo));
        SourceOrderLineNo = WmsText.Required(sourceOrderLineNo, nameof(sourceOrderLineNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        FromLocationCode = WmsText.Required(fromLocationCode, nameof(fromLocationCode));
        ToLocationCode = WmsText.Required(toLocationCode, nameof(toLocationCode));
        LotNo = WmsText.Optional(lotNo);
        SerialNo = WmsText.Optional(serialNo);
        AssignedOperatorUserId = WmsText.Optional(assignedOperatorUserId);
        AssignedPoolCode = WmsText.Optional(assignedPoolCode);
        PlannedQuantity = WmsText.Positive(plannedQuantity, nameof(plannedQuantity));
        Status = WarehouseTaskStatus.Open;
        ExecutionChannel = WarehouseTaskExecutionChannel.Unclaimed;
        Version = 1;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public WarehouseTaskType TaskType { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string TaskNo { get; private set; } = string.Empty;
    public string SourceOrderNo { get; private set; } = string.Empty;
    public string SourceOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string FromLocationCode { get; private set; } = string.Empty;
    public string ToLocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string? AssignedOperatorUserId { get; private set; }
    public string? AssignedPoolCode { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public decimal ExecutedQuantity { get; private set; }
    public WarehouseTaskStatus Status { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? ExceptionCode { get; private set; }
    public string? ExceptionReason { get; private set; }
    public DateTime? ExceptionAtUtc { get; private set; }
    public string? ExceptionBy { get; private set; }
    public string? CompletedBy { get; private set; }
    public string? CompletionReason { get; private set; }
    public WarehouseTaskExecutionChannel ExecutionChannel { get; private set; }
    public string? ExecutionClaimedBy { get; private set; }
    public DateTime? ExecutionClaimedAtUtc { get; private set; }

    public static WarehouseTask CreatePutaway(
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string fromLocationCode,
        string toLocationCode,
        decimal plannedQuantity,
        string? lotNo = null,
        string? serialNo = null,
        string? assignedOperatorUserId = null,
        string? assignedPoolCode = null)
    {
        return new WarehouseTask(
            WarehouseTaskType.Putaway,
            organizationId,
            environmentId,
            taskNo,
            sourceOrderNo,
            sourceOrderLineNo,
            skuCode,
            uomCode,
            siteCode,
            fromLocationCode,
            toLocationCode,
            plannedQuantity,
            lotNo,
            serialNo,
            assignedOperatorUserId,
            assignedPoolCode);
    }

    public static WarehouseTask CreatePicking(
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string fromLocationCode,
        string toLocationCode,
        decimal plannedQuantity,
        string? lotNo = null,
        string? serialNo = null,
        string? assignedOperatorUserId = null,
        string? assignedPoolCode = null)
    {
        return new WarehouseTask(
            WarehouseTaskType.Picking,
            organizationId,
            environmentId,
            taskNo,
            sourceOrderNo,
            sourceOrderLineNo,
            skuCode,
            uomCode,
            siteCode,
            fromLocationCode,
            toLocationCode,
            plannedQuantity,
            lotNo,
            serialNo,
            assignedOperatorUserId,
            assignedPoolCode);
    }

    public static WarehouseTask CreateReplenishment(
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string toLocationCode,
        decimal plannedQuantity,
        string? lotNo = null,
        string? serialNo = null,
        string? assignedOperatorUserId = null,
        string? assignedPoolCode = null)
    {
        return new WarehouseTask(
            WarehouseTaskType.Replenishment,
            organizationId,
            environmentId,
            taskNo,
            sourceOrderNo,
            sourceOrderLineNo,
            skuCode,
            uomCode,
            siteCode,
            "REPLENISHMENT-SOURCE-PENDING",
            toLocationCode,
            plannedQuantity,
            lotNo,
            serialNo,
            assignedOperatorUserId,
            assignedPoolCode);
    }

    public void Assign(
        string assignedPoolCode,
        string? assignedOperatorUserId,
        long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.Open);
        EnsureUnclaimedExecution();
        AssignedPoolCode = WmsText.Required(assignedPoolCode, nameof(assignedPoolCode));
        AssignedOperatorUserId = WmsText.Optional(assignedOperatorUserId);
        AdvanceVersion();
    }

    public void ClaimPoolAssignment(string actorUserId, long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.Open);
        EnsureUnclaimedExecution();
        if (string.IsNullOrWhiteSpace(AssignedPoolCode))
        {
            throw new InvalidOperationException("Warehouse task has no work-pool assignment to claim.");
        }

        var actor = WmsText.Required(actorUserId, nameof(actorUserId));
        if (!string.IsNullOrWhiteSpace(AssignedOperatorUserId)
            && !string.Equals(AssignedOperatorUserId, actor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Warehouse task is assigned to another operator.");
        }

        AssignedOperatorUserId = actor;
        AdvanceVersion();
    }

    public void ClaimManualExecution(string actorUserId, long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.Open);
        EnsureHasPoolAssignment();
        var actor = WmsText.Required(actorUserId, nameof(actorUserId));
        if (!string.Equals(AssignedOperatorUserId, actor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Warehouse task is not assigned to this operator.");
        }

        ClaimExecution(WarehouseTaskExecutionChannel.Manual, actor);
        AdvanceVersion();
    }

    public void ClaimWcsExecution(string claimReference, long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.Open);
        EnsureHasPoolAssignment();
        ClaimExecution(WarehouseTaskExecutionChannel.Wcs, claimReference);
        Status = WarehouseTaskStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
        AdvanceVersion();
    }

    public void ValidateWcsExecution(string claimReference, long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        ValidateWcsExecution(claimReference);
    }

    public void ValidateWcsExecution(string claimReference)
    {
        EnsureStatus(WarehouseTaskStatus.InProgress);
        EnsureWcsClaim(claimReference);
    }

    public void Start(
        string actorUserId,
        long expectedVersion,
        bool claimPoolAssignment = false)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.Open);
        EnsureHasPoolAssignment();
        var actor = WmsText.Required(actorUserId, nameof(actorUserId));
        if (claimPoolAssignment)
        {
            if (string.IsNullOrWhiteSpace(AssignedPoolCode))
            {
                throw new InvalidOperationException("Warehouse task has no work-pool assignment to claim.");
            }

            if (!string.IsNullOrWhiteSpace(AssignedOperatorUserId)
                && !string.Equals(AssignedOperatorUserId, actor, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Warehouse task is assigned to another operator.");
            }

            AssignedOperatorUserId = actor;
        }
        else if (!string.Equals(AssignedOperatorUserId, actor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Warehouse task is not assigned to this operator.");
        }

        ClaimExecution(WarehouseTaskExecutionChannel.Manual, actor);
        Status = WarehouseTaskStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
        AdvanceVersion();
    }

    public void RecordProgress(decimal executedQuantity, string actorUserId, long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.InProgress);
        EnsureManualActor(actorUserId);
        EnsureProgress(executedQuantity);
        ExecutedQuantity = executedQuantity;
        AdvanceVersion();
    }

    public void ReportException(
        string exceptionCode,
        string reason,
        string actorUserId,
        long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureActive();
        EnsureManualActor(actorUserId);
        ExceptionCode = WmsText.Required(exceptionCode, nameof(exceptionCode));
        ExceptionReason = WmsText.Required(reason, nameof(reason));
        ExceptionBy = WmsText.Required(actorUserId, nameof(actorUserId));
        ExceptionAtUtc = DateTime.UtcNow;
        Status = WarehouseTaskStatus.Exception;
        AdvanceVersion();
    }

    public void Complete(
        decimal executedQuantity,
        string actorUserId,
        string? completionReason,
        long expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        EnsureStatus(WarehouseTaskStatus.InProgress);
        EnsureManualActor(actorUserId);
        EnsureProgress(executedQuantity);
        var completedBy = WmsText.Required(actorUserId, nameof(actorUserId));
        var normalizedReason = WmsText.Optional(completionReason);
        if (TaskType != WarehouseTaskType.Picking && executedQuantity != PlannedQuantity)
        {
            throw new InvalidOperationException("Putaway and replenishment tasks require full planned quantity before completion.");
        }

        if (TaskType == WarehouseTaskType.Picking
            && executedQuantity < PlannedQuantity
            && normalizedReason is null)
        {
            throw new ArgumentException("A completion reason is required for a picking difference.", nameof(completionReason));
        }

        ExecutedQuantity = executedQuantity;
        Status = executedQuantity == PlannedQuantity
            ? WarehouseTaskStatus.Completed
            : WarehouseTaskStatus.CompletedWithDifference;
        CompletedAtUtc = DateTime.UtcNow;
        CompletedBy = completedBy;
        CompletionReason = normalizedReason;
        AdvanceVersion();
    }

    // Compatibility path for the existing internal WCS callbacks. New human-work
    // commands must use the actor/version overload above.
    public void RecordProgress(decimal executedQuantity)
    {
        EnsureActive();
        EnsureProgress(executedQuantity);
        ExecutedQuantity = executedQuantity;
        if (ExecutedQuantity == PlannedQuantity)
        {
            Status = WarehouseTaskStatus.Completed;
            CompletedAtUtc = DateTime.UtcNow;
            CompletedBy = "system:wcs";
        }

        AdvanceVersion();
    }

    public void RecordWcsProgress(decimal executedQuantity, string claimReference)
    {
        EnsureActive();
        EnsureWcsClaim(claimReference);
        EnsureProgress(executedQuantity);
        ExecutedQuantity = executedQuantity;
        if (ExecutedQuantity == PlannedQuantity)
        {
            Status = WarehouseTaskStatus.Completed;
            CompletedAtUtc = DateTime.UtcNow;
            CompletedBy = $"system:wcs:{WmsText.Required(claimReference, nameof(claimReference))}";
        }

        AdvanceVersion();
    }

    public void Cancel()
    {
        EnsureActive();
        Status = WarehouseTaskStatus.Cancelled;
        AdvanceVersion();
    }

    private void EnsureProgress(decimal executedQuantity)
    {
        if (executedQuantity < 0 || executedQuantity > PlannedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(executedQuantity), executedQuantity, "Executed quantity must be within planned quantity.");
        }

        if (executedQuantity < ExecutedQuantity)
        {
            throw new InvalidOperationException("Warehouse task progress cannot regress.");
        }
    }

    private void EnsureActive()
    {
        if (Status is not (WarehouseTaskStatus.Open or WarehouseTaskStatus.InProgress))
        {
            throw new InvalidOperationException($"Warehouse task is terminal in status '{Status}'.");
        }
    }

    private void EnsureStatus(WarehouseTaskStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                IsTerminal(Status)
                    ? $"Warehouse task is terminal in status '{Status}'."
                    : $"Warehouse task must be '{expected}' but is '{Status}'.");
        }
    }

    private void EnsureUnclaimedExecution()
    {
        if (ExecutionChannel is not (
            WarehouseTaskExecutionChannel.LegacyUnclaimed
            or WarehouseTaskExecutionChannel.Unclaimed))
        {
            throw new InvalidOperationException(
                $"Warehouse task execution is already claimed by '{ExecutionChannel}'.");
        }
    }

    private void ClaimExecution(WarehouseTaskExecutionChannel channel, string claimedBy)
    {
        var actor = WmsText.Required(claimedBy, nameof(claimedBy));
        if (ExecutionChannel == channel
            && string.Equals(ExecutionClaimedBy, actor, StringComparison.Ordinal))
        {
            return;
        }

        EnsureUnclaimedExecution();
        ExecutionChannel = channel;
        ExecutionClaimedBy = actor;
        ExecutionClaimedAtUtc = DateTime.UtcNow;
    }

    private void EnsureManualActor(string actorUserId)
    {
        var actor = WmsText.Required(actorUserId, nameof(actorUserId));
        if (ExecutionChannel != WarehouseTaskExecutionChannel.Manual
            || !string.Equals(ExecutionClaimedBy, actor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Warehouse task is not claimed for this manual operator.");
        }
    }

    private void EnsureWcsClaim(string claimReference)
    {
        var claim = WmsText.Required(claimReference, nameof(claimReference));
        if (ExecutionChannel != WarehouseTaskExecutionChannel.Wcs
            || !string.Equals(ExecutionClaimedBy, claim, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Warehouse task is not claimed for this WCS task.");
        }
    }

    private void EnsureHasPoolAssignment()
    {
        if (string.IsNullOrWhiteSpace(AssignedPoolCode))
        {
            throw new InvalidOperationException(
                "Warehouse task has no persisted work-pool assignment.");
        }
    }

    private void EnsureExpectedVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Warehouse task version conflict: expected {expectedVersion}, actual {Version}.");
        }
    }

    private void AdvanceVersion()
    {
        Version = checked(Version + 1);
    }

    private static bool IsTerminal(WarehouseTaskStatus status) =>
        status is WarehouseTaskStatus.Completed
            or WarehouseTaskStatus.CompletedWithDifference
            or WarehouseTaskStatus.Exception
            or WarehouseTaskStatus.Cancelled;
}
