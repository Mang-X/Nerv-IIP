namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;

public partial record ScheduleOperationOverrideId : IGuidStronglyTypedId;

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

    public static ScheduleOperationOverride Create(
        string organizationId, string environmentId, string workOrderId, string operationId,
        int operationSequence, string resourceId, string workCenterId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, string lockReasonCode,
        string sourceType, string? sourceEventId, string actor,
        DateTimeOffset sourceOccurredAtUtc, DateTimeOffset updatedAtUtc) =>
        new(organizationId, environmentId, workOrderId, operationId, operationSequence,
            resourceId, workCenterId, startUtc, endUtc, lockReasonCode, sourceType,
            sourceEventId, actor, sourceOccurredAtUtc, updatedAtUtc);

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

    public void ReplaceManually(
        string resourceId, string workCenterId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        string actor, DateTimeOffset occurredAtUtc)
    {
        SetMutableFacts(resourceId, workCenterId, startUtc, endUtc, "manual-override",
            "scheduling-api", null, actor, occurredAtUtc, occurredAtUtc);
    }

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
