using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

public partial record WcsTaskId : IGuidStronglyTypedId;

public enum WcsTaskStatus
{
    Dispatched = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
}

public sealed class WcsTask : Entity<WcsTaskId>, IAggregateRoot
{
    public const int MaxRetryAttempts = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);

    private WcsTask()
    {
    }

    private WcsTask(string organizationId, string environmentId, WarehouseTaskId warehouseTaskId, string adapterType, string externalTaskId, string payloadJson, string? deviceId)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        WarehouseTaskId = warehouseTaskId;
        AdapterType = WmsText.Required(adapterType, nameof(adapterType)).ToLowerInvariant();
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? AdapterType : WmsText.Required(deviceId, nameof(deviceId));
        ExternalTaskId = WmsText.Required(externalTaskId, nameof(externalTaskId));
        PayloadJson = WmsText.Required(payloadJson, nameof(payloadJson));
        Status = WcsTaskStatus.Dispatched;
        AttemptCount = 1;
        DispatchedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new WcsTaskDispatchedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public WarehouseTaskId WarehouseTaskId { get; private set; } = default!;
    public string AdapterType { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public string ExternalTaskId { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public WcsTaskStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? CompletionPayloadJson { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTime DispatchedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public bool IsTerminalFailure { get; private set; }

    public static WcsTask Dispatch(string organizationId, string environmentId, WarehouseTaskId warehouseTaskId, string adapterType, string externalTaskId, string payloadJson, string? deviceId = null)
    {
        return new WcsTask(organizationId, environmentId, warehouseTaskId, adapterType, externalTaskId, payloadJson, deviceId);
    }

    public bool IsSameDispatch(WcsTask other)
    {
        return WarehouseTaskId == other.WarehouseTaskId && AdapterType == other.AdapterType;
    }

    public void Complete(string completionPayloadJson)
    {
        if (Status == WcsTaskStatus.Failed)
        {
            throw new InvalidOperationException("Failed WCS tasks must be retried before completion.");
        }

        CompletionPayloadJson = WmsText.Required(completionPayloadJson, nameof(completionPayloadJson));
        Status = WcsTaskStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        NextRetryAtUtc = null;
        this.AddDomainEvent(new WcsTaskCompletedDomainEvent(this));
    }

    public void Fail(
        string failureCode,
        string failureMessage,
        DateTime? failedAtUtc = null,
        int maxRetryAttempts = MaxRetryAttempts,
        TimeSpan? initialRetryDelay = null)
    {
        if (Status == WcsTaskStatus.Completed)
        {
            throw new InvalidOperationException("Completed WCS tasks cannot later fail.");
        }

        FailureCode = WmsText.Required(failureCode, nameof(failureCode));
        FailureMessage = WmsText.Required(failureMessage, nameof(failureMessage));
        Status = WcsTaskStatus.Failed;
        FailedAtUtc = EnsureUtc(failedAtUtc ?? DateTime.UtcNow);
        if (maxRetryAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryAttempts));
        }

        IsTerminalFailure = AttemptCount >= maxRetryAttempts;
        NextRetryAtUtc = IsTerminalFailure ? null : FailedAtUtc.Value.Add(BackoffFor(AttemptCount, initialRetryDelay ?? InitialRetryDelay));
        this.AddDomainEvent(new WcsTaskFailedDomainEvent(this));
        if (IsTerminalFailure)
        {
            this.AddDomainEvent(new WcsTaskRetryExhaustedDomainEvent(this));
        }
    }

    public void Retry(string externalTaskId, string payloadJson, DateTime? retriedAtUtc = null)
    {
        if (Status != WcsTaskStatus.Failed)
        {
            throw new InvalidOperationException("Only failed WCS tasks can be retried.");
        }

        if (IsTerminalFailure)
        {
            throw new InvalidOperationException("WCS task retry limit has been reached.");
        }

        var retryAtUtc = EnsureUtc(retriedAtUtc ?? DateTime.UtcNow);
        if (NextRetryAtUtc is not null && retryAtUtc < NextRetryAtUtc.Value)
        {
            throw new InvalidOperationException($"WCS task retry is not due until {NextRetryAtUtc:O}.");
        }

        ExternalTaskId = WmsText.Required(externalTaskId, nameof(externalTaskId));
        PayloadJson = WmsText.Required(payloadJson, nameof(payloadJson));
        Status = WcsTaskStatus.Dispatched;
        AttemptCount++;
        DispatchedAtUtc = retryAtUtc;
        NextRetryAtUtc = null;
        this.AddDomainEvent(new WcsTaskDispatchedDomainEvent(this));
    }

    private static TimeSpan BackoffFor(int attemptCount, TimeSpan initialRetryDelay) =>
        TimeSpan.FromTicks(initialRetryDelay.Ticks * (1L << (attemptCount - 1)));

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : value.ToUniversalTime();

    public void Cancel()
    {
        if (Status == WcsTaskStatus.Completed)
        {
            throw new InvalidOperationException("Completed WCS tasks cannot be cancelled.");
        }

        Status = WcsTaskStatus.Cancelled;
        this.AddDomainEvent(new WcsTaskCancelledDomainEvent(this));
    }
}
