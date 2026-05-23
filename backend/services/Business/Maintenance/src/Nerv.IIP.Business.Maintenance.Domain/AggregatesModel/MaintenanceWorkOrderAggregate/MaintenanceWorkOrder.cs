using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

public partial record MaintenanceWorkOrderId : IGuidStronglyTypedId;

public partial record SparePartLineId : IGuidStronglyTypedId;

public enum MaintenanceWorkOrderStatus
{
    Open = 0,
    Completed = 1,
}

public sealed record SparePartLineDraft(string SkuCode, decimal Quantity, string? UomCode = null);

public sealed class MaintenanceWorkOrder : Entity<MaintenanceWorkOrderId>, IAggregateRoot
{
    private readonly List<SparePartLine> sparePartLines = [];

    private MaintenanceWorkOrder()
    {
    }

    private MaintenanceWorkOrder(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string priority,
        string? sourceAlarmId,
        string openedBy)
    {
        Id = new MaintenanceWorkOrderId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = MaintenanceText.Required(deviceAssetId, nameof(deviceAssetId));
        Priority = MaintenanceText.Required(priority, nameof(priority)).ToLowerInvariant();
        SourceAlarmId = MaintenanceText.Optional(sourceAlarmId);
        OpenedBy = MaintenanceText.Required(openedBy, nameof(openedBy));
        Status = MaintenanceWorkOrderStatus.Open;
        OpenedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new MaintenanceWorkOrderOpenedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string Priority { get; private set; } = string.Empty;
    public string? SourceAlarmId { get; private set; }
    public string OpenedBy { get; private set; } = string.Empty;
    public MaintenanceWorkOrderStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public bool AssetUnavailable { get; private set; }
    public string? AssetUnavailableReason { get; private set; }
    public DateTimeOffset? AssetUnavailableFromUtc { get; private set; }
    public string? CompletionResult { get; private set; }
    public string? DowntimeReasonCode { get; private set; }
    public int? DowntimeMinutes { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<SparePartLine> SparePartLines => sparePartLines;

    public static MaintenanceWorkOrder OpenManual(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string priority,
        string openedBy)
    {
        return new MaintenanceWorkOrder(organizationId, environmentId, deviceAssetId, priority, null, openedBy);
    }

    public static MaintenanceWorkOrder OpenFromAlarm(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string sourceAlarmId,
        string priority,
        string openedBy = "industrialTelemetry")
    {
        return new MaintenanceWorkOrder(organizationId, environmentId, deviceAssetId, priority, MaintenanceText.Required(sourceAlarmId, nameof(sourceAlarmId)), openedBy);
    }

    public void MarkAssetUnavailable(DateTimeOffset fromUtc, string reason)
    {
        EnsureOpen();
        var normalizedReason = MaintenanceText.Required(reason, nameof(reason));
        if (AssetUnavailable)
        {
            return;
        }

        AssetUnavailable = true;
        AssetUnavailableReason = normalizedReason;
        AssetUnavailableFromUtc = fromUtc;
        this.AddDomainEvent(new AssetUnavailableDomainEvent(this, normalizedReason, fromUtc));
    }

    public void Complete(string result, string downtimeReasonCode, int downtimeMinutes, IEnumerable<SparePartLineDraft> spareParts)
    {
        EnsureOpen();
        CompletionResult = MaintenanceText.Required(result, nameof(result));
        DowntimeReasonCode = MaintenanceText.Required(downtimeReasonCode, nameof(downtimeReasonCode));
        DowntimeMinutes = MaintenanceText.Positive(downtimeMinutes, nameof(downtimeMinutes));
        sparePartLines.Clear();
        foreach (var part in spareParts)
        {
            sparePartLines.Add(SparePartLine.Create(part));
        }

        Status = MaintenanceWorkOrderStatus.Completed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new MaintenanceWorkOrderCompletedDomainEvent(this));
        if (AssetUnavailable)
        {
            this.AddDomainEvent(new AssetRestoredDomainEvent(this, CompletedAtUtc.Value));
        }
    }

    private void EnsureOpen()
    {
        if (Status == MaintenanceWorkOrderStatus.Completed)
        {
            throw new InvalidOperationException("Completed maintenance work orders are immutable.");
        }
    }
}

public sealed class SparePartLine : Entity<SparePartLineId>
{
    private SparePartLine()
    {
    }

    private SparePartLine(SparePartLineDraft draft)
    {
        Id = new SparePartLineId(Guid.CreateVersion7());
        SkuCode = MaintenanceText.Required(draft.SkuCode, nameof(draft.SkuCode));
        Quantity = MaintenanceText.Positive(draft.Quantity, nameof(draft.Quantity));
        UomCode = MaintenanceText.Optional(draft.UomCode);
    }

    public string SkuCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string? UomCode { get; private set; }

    public static SparePartLine Create(SparePartLineDraft draft)
    {
        return new SparePartLine(draft);
    }
}
