namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

public partial record OperationTaskId : IGuidStronglyTypedId;

public enum OperationTaskLifecycleStatus
{
    Queued,
    InProgress,
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
        DateTimeOffset? existingEndUtc)
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
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string OperationTaskId => OperationTaskIdValue;

    public TimeSpan Duration => TimeSpan.FromTicks(DurationTicks);

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
        TimeSpan duration)
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
            null);
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
        DateTimeOffset? existingEndUtc)
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
            existingEndUtc);
    }

    private static string NormalizeAlternatives(IReadOnlyCollection<string> values)
    {
        return string.Join('|', values.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
