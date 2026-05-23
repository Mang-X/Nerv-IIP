using System.Text.Json;
using System.Text.Json.Serialization;

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
    private IReadOnlyCollection<ScheduledOperationSnapshot>? assignments;
    private IReadOnlyCollection<string>? affectedWorkOrderIds;

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
        AssignmentsJson = SerializeVersioned(assignments.OrderBy(x => x.StartUtc).ThenBy(x => x.OperationTaskId).ToList());
        AffectedWorkOrderIdsJson = SerializeVersioned(affectedWorkOrderIds.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public int ScheduleVersion { get; private set; }
    public ScheduleTrigger Trigger { get; private set; }
    public DateTimeOffset ScheduledAtUtc { get; private set; }
    public string AssignmentsJson { get; private set; } = """{"_v":1,"items":[]}""";
    public string AffectedWorkOrderIdsJson { get; private set; } = """{"_v":1,"items":[]}""";

    public IReadOnlyCollection<ScheduledOperationSnapshot> Assignments =>
        assignments ??= DeserializeVersioned<ScheduledOperationSnapshot>(AssignmentsJson);

    public IReadOnlyCollection<string> AffectedWorkOrderIds =>
        affectedWorkOrderIds ??= DeserializeVersioned<string>(AffectedWorkOrderIdsJson);

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

    private static string SerializeVersioned<T>(IReadOnlyCollection<T> items)
    {
        return JsonSerializer.Serialize(new VersionedJson<T>(1, items), SerializerOptions);
    }

    private static IReadOnlyCollection<T> DeserializeVersioned<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<IReadOnlyCollection<T>>(json, SerializerOptions) ?? [];
        }

        var payload = JsonSerializer.Deserialize<VersionedJson<T>>(json, SerializerOptions);
        return payload?.Items ?? [];
    }

    private sealed record VersionedJson<T>(
        [property: JsonPropertyName("_v")] int Version,
        IReadOnlyCollection<T> Items);
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
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
        FromUtc = fromUtc;
        ToUtc = toUtc;
        Reason = DomainGuard.Required(reason, nameof(reason));
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
        DeviceAssetId = DomainGuard.Required(deviceAssetId, nameof(deviceAssetId));
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
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
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
    }
}
