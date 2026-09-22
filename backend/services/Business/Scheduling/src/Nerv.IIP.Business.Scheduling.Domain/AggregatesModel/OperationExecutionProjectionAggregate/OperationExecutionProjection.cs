namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;

public partial record OperationExecutionProjectionId : IGuidStronglyTypedId;
public partial record OperationExecutionDowntimeStateId : IGuidStronglyTypedId;

public sealed class OperationExecutionProjection : Entity<OperationExecutionProjectionId>, IAggregateRoot
{
    private readonly List<OperationExecutionDowntimeState> downtimeStates = [];

    private OperationExecutionProjection()
    {
    }

    private OperationExecutionProjection(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        int? operationSequence,
        string? workCenterId,
        DateTimeOffset sourceOccurredAtUtc,
        string sourceEventId)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        WorkOrderId = Required(workOrderId);
        OperationId = Required(operationId);
        OperationSequence = operationSequence;
        WorkCenterId = Optional(workCenterId);
        LatestSourceOccurredAtUtc = sourceOccurredAtUtc;
        LatestSourceEventId = Required(sourceEventId);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public int? OperationSequence { get; private set; }
    public string? WorkCenterId { get; private set; }
    public DateTimeOffset? ActualStartedAtUtc { get; private set; }
    public DateTimeOffset? ActualCompletedAtUtc { get; private set; }
    public bool IsPaused { get; private set; }
    public decimal CompletedQuantity { get; private set; }
    public bool IsDowntimeBlocked { get; private set; }
    public bool IsQualityBlocked { get; private set; }
    public DateTimeOffset? LifecycleOccurredAtUtc { get; private set; }
    public string? LifecycleEventId { get; private set; }
    public DateTimeOffset? DowntimeOccurredAtUtc { get; private set; }
    public string? DowntimeEventId { get; private set; }
    public DateTimeOffset? QualityOccurredAtUtc { get; private set; }
    public string? QualityEventId { get; private set; }
    public DateTimeOffset LatestSourceOccurredAtUtc { get; private set; }
    public string LatestSourceEventId { get; private set; } = string.Empty;
    public IReadOnlyCollection<OperationExecutionDowntimeState> DowntimeStates => downtimeStates;

    public static OperationExecutionProjection Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        int? operationSequence,
        string? workCenterId,
        DateTimeOffset sourceOccurredAtUtc,
        string sourceEventId) =>
        new(
            organizationId,
            environmentId,
            workOrderId,
            operationId,
            operationSequence,
            workCenterId,
            sourceOccurredAtUtc,
            sourceEventId);

    public void EnrichIdentity(int? operationSequence, string? workCenterId)
    {
        OperationSequence ??= operationSequence;
        WorkCenterId ??= Optional(workCenterId);
    }

    public void ApplyStarted(DateTimeOffset occurredAtUtc, string eventId)
    {
        if (ActualStartedAtUtc is null || occurredAtUtc < ActualStartedAtUtc)
        {
            ActualStartedAtUtc = occurredAtUtc;
        }

        ApplyLifecycleState(occurredAtUtc, eventId, paused: false);
    }

    public void ApplyPaused(DateTimeOffset occurredAtUtc, string eventId) =>
        ApplyLifecycleState(occurredAtUtc, eventId, paused: true);

    public void ApplyResumed(DateTimeOffset occurredAtUtc, string eventId) =>
        ApplyLifecycleState(occurredAtUtc, eventId, paused: false);

    public void ApplyCompleted(DateTimeOffset occurredAtUtc, string eventId)
    {
        if (!CanApply(occurredAtUtc, LifecycleOccurredAtUtc))
        {
            return;
        }

        ActualCompletedAtUtc = occurredAtUtc;
        SetLifecycleState(occurredAtUtc, eventId, paused: false);
    }

    public void AddCompletedQuantity(decimal quantityDelta, DateTimeOffset occurredAtUtc, string eventId)
    {
        CompletedQuantity += quantityDelta;
        UpdateLatestSource(occurredAtUtc, eventId);
    }

    public void ApplyDowntimeStarted(string downtimeEventNo, DateTimeOffset occurredAtUtc, string eventId) =>
        ApplyDowntimeState(downtimeEventNo, occurredAtUtc, eventId, active: true);

    public void ApplyDowntimeRestored(string downtimeEventNo, DateTimeOffset occurredAtUtc, string eventId) =>
        ApplyDowntimeState(downtimeEventNo, occurredAtUtc, eventId, active: false);

    public void ApplyQualityBlocked(DateTimeOffset occurredAtUtc, string eventId) =>
        ApplyQualityState(occurredAtUtc, eventId, blocked: true);

    public void ApplyQualityReleased(DateTimeOffset occurredAtUtc, string eventId) =>
        ApplyQualityState(occurredAtUtc, eventId, blocked: false);

    private void ApplyLifecycleState(DateTimeOffset occurredAtUtc, string eventId, bool paused)
    {
        if (!CanApply(occurredAtUtc, LifecycleOccurredAtUtc))
        {
            return;
        }

        SetLifecycleState(occurredAtUtc, eventId, paused);
    }

    private void SetLifecycleState(DateTimeOffset occurredAtUtc, string eventId, bool paused)
    {
        IsPaused = paused;
        LifecycleOccurredAtUtc = occurredAtUtc;
        LifecycleEventId = Required(eventId);
        UpdateLatestSource(occurredAtUtc, eventId);
    }

    private void ApplyDowntimeState(
        string downtimeEventNo,
        DateTimeOffset occurredAtUtc,
        string eventId,
        bool active)
    {
        var normalizedDowntimeEventNo = Required(downtimeEventNo);
        var state = downtimeStates.SingleOrDefault(x => x.DowntimeEventNo == normalizedDowntimeEventNo);
        var applied = state is null
            ? AddDowntimeState(normalizedDowntimeEventNo, occurredAtUtc, eventId, active)
            : active
                ? state.ApplyStarted(occurredAtUtc, eventId)
                : state.ApplyRestored(occurredAtUtc, eventId);
        if (!applied)
        {
            return;
        }

        IsDowntimeBlocked = downtimeStates.Any(x => x.IsActive);
        if (CanApply(occurredAtUtc, DowntimeOccurredAtUtc))
        {
            DowntimeOccurredAtUtc = occurredAtUtc;
            DowntimeEventId = Required(eventId);
        }
        UpdateLatestSource(occurredAtUtc, eventId);
    }

    private bool AddDowntimeState(
        string downtimeEventNo,
        DateTimeOffset occurredAtUtc,
        string eventId,
        bool active)
    {
        downtimeStates.Add(OperationExecutionDowntimeState.Create(
            downtimeEventNo,
            active,
            occurredAtUtc,
            eventId));
        return true;
    }

    private void ApplyQualityState(DateTimeOffset occurredAtUtc, string eventId, bool blocked)
    {
        if (!CanApply(occurredAtUtc, QualityOccurredAtUtc))
        {
            return;
        }

        IsQualityBlocked = blocked;
        QualityOccurredAtUtc = occurredAtUtc;
        QualityEventId = Required(eventId);
        UpdateLatestSource(occurredAtUtc, eventId);
    }

    private void UpdateLatestSource(DateTimeOffset occurredAtUtc, string eventId)
    {
        if (occurredAtUtc < LatestSourceOccurredAtUtc)
        {
            return;
        }

        LatestSourceOccurredAtUtc = occurredAtUtc;
        LatestSourceEventId = Required(eventId);
    }

    private static bool CanApply(DateTimeOffset occurredAtUtc, DateTimeOffset? watermark) =>
        watermark is null || occurredAtUtc >= watermark.Value;

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OperationExecutionDowntimeState : Entity<OperationExecutionDowntimeStateId>
{
    private OperationExecutionDowntimeState()
    {
    }

    private OperationExecutionDowntimeState(
        string downtimeEventNo,
        bool isActive,
        DateTimeOffset occurredAtUtc,
        string sourceEventId)
    {
        DowntimeEventNo = Required(downtimeEventNo);
        IsActive = isActive;
        OccurredAtUtc = occurredAtUtc;
        SourceEventId = Required(sourceEventId);
    }

    public OperationExecutionProjectionId OperationExecutionProjectionId { get; private set; } = null!;
    public string DowntimeEventNo { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;

    internal static OperationExecutionDowntimeState Create(
        string downtimeEventNo,
        bool isActive,
        DateTimeOffset occurredAtUtc,
        string sourceEventId) =>
        new(downtimeEventNo, isActive, occurredAtUtc, sourceEventId);

    internal bool ApplyStarted(DateTimeOffset occurredAtUtc, string sourceEventId)
    {
        if (occurredAtUtc < OccurredAtUtc || (occurredAtUtc == OccurredAtUtc && !IsActive))
        {
            return false;
        }

        return Apply(active: true, occurredAtUtc, sourceEventId);
    }

    internal bool ApplyRestored(DateTimeOffset occurredAtUtc, string sourceEventId)
    {
        if (occurredAtUtc < OccurredAtUtc)
        {
            return false;
        }

        return Apply(active: false, occurredAtUtc, sourceEventId);
    }

    private bool Apply(bool active, DateTimeOffset occurredAtUtc, string sourceEventId)
    {
        IsActive = active;
        OccurredAtUtc = occurredAtUtc;
        SourceEventId = Required(sourceEventId);
        return true;
    }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();
}
