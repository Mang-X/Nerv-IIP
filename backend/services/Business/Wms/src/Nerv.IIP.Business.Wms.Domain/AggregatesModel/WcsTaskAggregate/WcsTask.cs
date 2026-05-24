using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

public partial record WcsTaskId : IGuidStronglyTypedId;

public enum WcsTaskStatus
{
    Dispatched = 0,
    Completed = 1,
    Failed = 2,
}

public sealed class WcsTask : Entity<WcsTaskId>, IAggregateRoot
{
    private WcsTask()
    {
    }

    private WcsTask(string organizationId, string environmentId, WarehouseTaskId warehouseTaskId, string adapterType, string externalTaskId, string payloadJson)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        WarehouseTaskId = warehouseTaskId;
        AdapterType = WmsText.Required(adapterType, nameof(adapterType)).ToLowerInvariant();
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

    public static WcsTask Dispatch(string organizationId, string environmentId, WarehouseTaskId warehouseTaskId, string adapterType, string externalTaskId, string payloadJson)
    {
        return new WcsTask(organizationId, environmentId, warehouseTaskId, adapterType, externalTaskId, payloadJson);
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
        this.AddDomainEvent(new WcsTaskCompletedDomainEvent(this));
    }

    public void Fail(string failureCode, string failureMessage)
    {
        if (Status == WcsTaskStatus.Completed)
        {
            throw new InvalidOperationException("Completed WCS tasks cannot later fail.");
        }

        FailureCode = WmsText.Required(failureCode, nameof(failureCode));
        FailureMessage = WmsText.Required(failureMessage, nameof(failureMessage));
        Status = WcsTaskStatus.Failed;
        FailedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new WcsTaskFailedDomainEvent(this));
    }

    public void Retry(string externalTaskId, string payloadJson)
    {
        if (Status != WcsTaskStatus.Failed)
        {
            throw new InvalidOperationException("Only failed WCS tasks can be retried.");
        }

        ExternalTaskId = WmsText.Required(externalTaskId, nameof(externalTaskId));
        PayloadJson = WmsText.Required(payloadJson, nameof(payloadJson));
        Status = WcsTaskStatus.Dispatched;
        AttemptCount++;
        DispatchedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new WcsTaskDispatchedDomainEvent(this));
    }
}
