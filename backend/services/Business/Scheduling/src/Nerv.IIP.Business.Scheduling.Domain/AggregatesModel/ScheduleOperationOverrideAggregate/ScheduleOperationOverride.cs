namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;

public partial record ScheduleOperationOverrideId : IGuidStronglyTypedId;

public static class ScheduleOperationOverrideLockReasonCodes
{
    public const string MesManualDispatch = "mes-manual-dispatch";
    public const string ManualOverride = "manual-override";
}

public static class ScheduleOperationOverrideSourceTypes
{
    public const string MesDispatch = "mes-dispatch";
    public const string SchedulingApi = "scheduling-api";
}

public sealed class ScheduleOperationOverride : Entity<ScheduleOperationOverrideId>, IAggregateRoot
{
    private ScheduleOperationOverride()
    {
    }

    private ScheduleOperationOverride(
        string organizationId, string environmentId, string workOrderId, string operationId,
        int operationSequence, string resourceId, string workCenterId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, string lockReasonCode,
        string sourceType, string? sourceEventId, string actor,
        DateTimeOffset sourceOccurredAtUtc, DateTimeOffset updatedAtUtc)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        WorkOrderId = Required(workOrderId);
        OperationId = Required(operationId);
        OperationSequence = operationSequence;
        SetMutableFacts(resourceId, workCenterId, startUtc, endUtc, lockReasonCode, sourceType,
            sourceEventId, actor, sourceOccurredAtUtc, updatedAtUtc);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public int OperationSequence { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public DateTimeOffset StartUtc { get; private set; }
    public DateTimeOffset EndUtc { get; private set; }
    public string LockReasonCode { get; private set; } = string.Empty;
    public string SourceType { get; private set; } = string.Empty;
    public string? SourceEventId { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset SourceOccurredAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long? SourceRevision { get; private set; }
    public string? ClearedReasonCode { get; private set; }
    public DateTimeOffset? ClearedAtUtc { get; private set; }

    public static ScheduleOperationOverride Create(
        string organizationId, string environmentId, string workOrderId, string operationId,
        int operationSequence, string resourceId, string workCenterId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, string lockReasonCode,
        string sourceType, string? sourceEventId, string actor,
        DateTimeOffset sourceOccurredAtUtc, DateTimeOffset updatedAtUtc) =>
        new(organizationId, environmentId, workOrderId, operationId, operationSequence,
            resourceId, workCenterId, startUtc, endUtc, lockReasonCode, sourceType,
            sourceEventId, actor, sourceOccurredAtUtc, updatedAtUtc);

    public static ScheduleOperationOverride CreateClearedMesDispatch(
        string organizationId, string environmentId, string workOrderId, string operationId,
        int operationSequence, string resourceId, string workCenterId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, string sourceEventId, string actor,
        long sourceRevision, DateTimeOffset sourceOccurredAtUtc, string clearedReasonCode,
        DateTimeOffset clearedAtUtc)
    {
        if (sourceRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRevision),
                "A cleared MES dispatch requires a positive source revision.");
        }

        clearedReasonCode = Required(clearedReasonCode);
        var fact = new ScheduleOperationOverride(
            organizationId, environmentId, workOrderId, operationId, operationSequence,
            resourceId, workCenterId, startUtc, endUtc,
            ScheduleOperationOverrideLockReasonCodes.MesManualDispatch,
            ScheduleOperationOverrideSourceTypes.MesDispatch,
            sourceEventId, actor, sourceOccurredAtUtc, clearedAtUtc)
        {
            IsActive = false,
            SourceRevision = sourceRevision,
            ClearedReasonCode = clearedReasonCode,
            ClearedAtUtc = clearedAtUtc
        };

        return fact;
    }

    public bool TryReplace(
        string resourceId, string workCenterId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        string lockReasonCode, string sourceType, string? sourceEventId, string actor,
        DateTimeOffset sourceOccurredAtUtc, DateTimeOffset updatedAtUtc)
    {
        if (sourceOccurredAtUtc < SourceOccurredAtUtc)
        {
            return false;
        }

        SetMutableFacts(resourceId, workCenterId, startUtc, endUtc, lockReasonCode, sourceType,
            sourceEventId, actor, sourceOccurredAtUtc, updatedAtUtc);
        return true;
    }

    public bool TryApplyMesDispatch(
        string resourceId, string workCenterId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        string sourceEventId, string actor, long sourceRevision,
        DateTimeOffset sourceOccurredAtUtc, DateTimeOffset updatedAtUtc)
    {
        if (!CanApplyMesFact(sourceRevision, sourceOccurredAtUtc))
        {
            return false;
        }

        SetMutableFacts(resourceId, workCenterId, startUtc, endUtc,
            ScheduleOperationOverrideLockReasonCodes.MesManualDispatch,
            ScheduleOperationOverrideSourceTypes.MesDispatch,
            sourceEventId, actor, sourceOccurredAtUtc, updatedAtUtc);
        IsActive = true;
        SourceRevision = PositiveRevisionOrNull(sourceRevision);
        ClearedReasonCode = null;
        ClearedAtUtc = null;
        return true;
    }

    public bool TryClearMesDispatch(
        long sourceRevision, string sourceEventId, string actor,
        DateTimeOffset sourceOccurredAtUtc, string clearedReasonCode,
        DateTimeOffset clearedAtUtc)
    {
        if (!string.Equals(SourceType, ScheduleOperationOverrideSourceTypes.MesDispatch, StringComparison.Ordinal) ||
            sourceRevision <= 0 ||
            !CanApplyMesFact(sourceRevision, sourceOccurredAtUtc))
        {
            return false;
        }

        IsActive = false;
        SourceEventId = Optional(sourceEventId);
        Actor = Required(actor);
        SourceRevision = sourceRevision;
        SourceOccurredAtUtc = sourceOccurredAtUtc;
        UpdatedAtUtc = clearedAtUtc;
        ClearedReasonCode = Required(clearedReasonCode);
        ClearedAtUtc = clearedAtUtc;
        return true;
    }

    public void ReplaceManually(
        string resourceId, string workCenterId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        string actor, DateTimeOffset occurredAtUtc)
    {
        SetMutableFacts(resourceId, workCenterId, startUtc, endUtc,
            ScheduleOperationOverrideLockReasonCodes.ManualOverride,
            ScheduleOperationOverrideSourceTypes.SchedulingApi,
            null, actor, occurredAtUtc, occurredAtUtc);
        IsActive = true;
        SourceRevision = null;
        ClearedReasonCode = null;
        ClearedAtUtc = null;
    }

    private bool CanApplyMesFact(long sourceRevision, DateTimeOffset sourceOccurredAtUtc)
    {
        if (string.Equals(SourceType, ScheduleOperationOverrideSourceTypes.MesDispatch, StringComparison.Ordinal) &&
            SourceRevision is > 0)
        {
            if (sourceRevision <= 0)
            {
                return false;
            }

            return sourceRevision > SourceRevision.Value;
        }

        return sourceOccurredAtUtc >= SourceOccurredAtUtc;
    }

    private static long? PositiveRevisionOrNull(long sourceRevision) =>
        sourceRevision > 0 ? sourceRevision : null;

    private void SetMutableFacts(
        string resourceId, string workCenterId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        string lockReasonCode, string sourceType, string? sourceEventId, string actor,
        DateTimeOffset sourceOccurredAtUtc, DateTimeOffset updatedAtUtc)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endUtc), "Override end must be after start.");
        }

        ResourceId = Required(resourceId);
        WorkCenterId = Required(workCenterId);
        StartUtc = startUtc;
        EndUtc = endUtc;
        LockReasonCode = Required(lockReasonCode);
        SourceType = Required(sourceType);
        SourceEventId = Optional(sourceEventId);
        Actor = Required(actor);
        SourceOccurredAtUtc = sourceOccurredAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
