using System.Text.Json;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;

public partial record ScheduleResultId : IGuidStronglyTypedId;

public partial record WorkCenterUnavailabilityId : IGuidStronglyTypedId;

public partial record DeviceAssetWorkCenterMappingId : IGuidStronglyTypedId;

public enum ScheduleTrigger
{
    Manual,
    RushOrder,
    AssetUnavailable,
    AssetRestored,
}

public sealed record ScheduledOperationSnapshot(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Reason);

public sealed class ScheduleResult : Entity<ScheduleResultId>, IAggregateRoot
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private ScheduleResult()
    {
    }

    private ScheduleResult(
        int scheduleVersion,
        ScheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        IReadOnlyCollection<ScheduledOperationSnapshot> assignments,
        IReadOnlyCollection<string> affectedWorkOrderIds)
    {
        ScheduleVersion = scheduleVersion > 0 ? scheduleVersion : throw new ArgumentOutOfRangeException(nameof(scheduleVersion));
        Trigger = trigger;
        ScheduledAtUtc = scheduledAtUtc;
        AssignmentsJson = JsonSerializer.Serialize(assignments.OrderBy(x => x.StartUtc).ThenBy(x => x.OperationTaskId), SerializerOptions);
        AffectedWorkOrderIdsJson = JsonSerializer.Serialize(affectedWorkOrderIds.Order(StringComparer.OrdinalIgnoreCase), SerializerOptions);
    }

    public int ScheduleVersion { get; private set; }
    public ScheduleTrigger Trigger { get; private set; }
    public DateTimeOffset ScheduledAtUtc { get; private set; }
    public string AssignmentsJson { get; private set; } = "[]";
    public string AffectedWorkOrderIdsJson { get; private set; } = "[]";

    public IReadOnlyCollection<ScheduledOperationSnapshot> Assignments =>
        JsonSerializer.Deserialize<IReadOnlyCollection<ScheduledOperationSnapshot>>(AssignmentsJson, SerializerOptions) ?? [];

    public IReadOnlyCollection<string> AffectedWorkOrderIds =>
        JsonSerializer.Deserialize<IReadOnlyCollection<string>>(AffectedWorkOrderIdsJson, SerializerOptions) ?? [];

    public static ScheduleResult Create(
        int scheduleVersion,
        ScheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        IReadOnlyCollection<ScheduledOperationSnapshot> assignments,
        IReadOnlyCollection<string> affectedWorkOrderIds)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(affectedWorkOrderIds);
        return new ScheduleResult(scheduleVersion, trigger, scheduledAtUtc, assignments, affectedWorkOrderIds);
    }
}

public sealed class WorkCenterUnavailability : Entity<WorkCenterUnavailabilityId>, IAggregateRoot
{
    private WorkCenterUnavailability()
    {
    }

    private WorkCenterUnavailability(
        string? organizationId,
        string? environmentId,
        string workCenterId,
        DateTimeOffset fromUtc,
        DateTimeOffset? toUtc,
        string reason,
        string? deviceAssetId)
    {
        OrganizationId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();
        EnvironmentId = string.IsNullOrWhiteSpace(environmentId) ? null : environmentId.Trim();
        WorkCenterId = Required(workCenterId);
        FromUtc = fromUtc;
        ToUtc = toUtc;
        Reason = Required(reason);
        DeviceAssetId = string.IsNullOrWhiteSpace(deviceAssetId) ? null : deviceAssetId.Trim();
    }

    public string WorkCenterId { get; private set; } = string.Empty;
    public string? OrganizationId { get; private set; }
    public string? EnvironmentId { get; private set; }
    public DateTimeOffset FromUtc { get; private set; }
    public DateTimeOffset? ToUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? DeviceAssetId { get; private set; }

    public static WorkCenterUnavailability Open(
        string? organizationId,
        string? environmentId,
        string workCenterId,
        DateTimeOffset fromUtc,
        DateTimeOffset? toUtc,
        string reason,
        string? deviceAssetId)
    {
        return new WorkCenterUnavailability(organizationId, environmentId, workCenterId, fromUtc, toUtc, reason, deviceAssetId);
    }

    public void Close(DateTimeOffset restoredAtUtc)
    {
        ToUtc = restoredAtUtc;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}

public sealed class DeviceAssetWorkCenterMapping : Entity<DeviceAssetWorkCenterMappingId>, IAggregateRoot
{
    private DeviceAssetWorkCenterMapping()
    {
    }

    private DeviceAssetWorkCenterMapping(string? organizationId, string? environmentId, string deviceAssetId, string workCenterId)
    {
        OrganizationId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();
        EnvironmentId = string.IsNullOrWhiteSpace(environmentId) ? null : environmentId.Trim();
        DeviceAssetId = Required(deviceAssetId);
        WorkCenterId = Required(workCenterId);
    }

    public string? OrganizationId { get; private set; }
    public string? EnvironmentId { get; private set; }
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;

    public static DeviceAssetWorkCenterMapping Create(string deviceAssetId, string workCenterId)
    {
        return new DeviceAssetWorkCenterMapping(null, null, deviceAssetId, workCenterId);
    }

    public static DeviceAssetWorkCenterMapping Create(string organizationId, string environmentId, string deviceAssetId, string workCenterId)
    {
        return new DeviceAssetWorkCenterMapping(organizationId, environmentId, deviceAssetId, workCenterId);
    }

    public void Remap(string workCenterId)
    {
        WorkCenterId = Required(workCenterId);
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
