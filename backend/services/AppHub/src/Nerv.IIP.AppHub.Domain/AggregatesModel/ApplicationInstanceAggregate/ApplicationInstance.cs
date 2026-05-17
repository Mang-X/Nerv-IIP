using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;

public partial record ApplicationInstanceId : IGuidStronglyTypedId;
public partial record InstanceHeartbeatId : IGuidStronglyTypedId;
public partial record InstanceStateHistoryId : IGuidStronglyTypedId;
public partial record InstanceStatusChangeId : IGuidStronglyTypedId;
public partial record RegistrationIdempotencyId : IGuidStronglyTypedId;

public class ApplicationInstance : Entity<ApplicationInstanceId>, IAggregateRoot
{
    protected ApplicationInstance()
    {
    }

    public ApplicationInstance(
        string organizationId,
        string environmentId,
        string applicationKey,
        string version,
        string nodeKey,
        string instanceKey,
        string instanceName,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<CapabilityDescriptor> capabilities)
    {
        Id = new ApplicationInstanceId(Guid.CreateVersion7());
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ApplicationKey = applicationKey;
        Version = version;
        NodeKey = nodeKey;
        InstanceKey = instanceKey;
        InstanceName = instanceName;
        Metadata = new Dictionary<string, string>(metadata);
        Capabilities = capabilities.ToList();
        this.AddDomainEvent(new ApplicationInstanceRegisteredDomainEvent(organizationId, environmentId, instanceKey));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ApplicationKey { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string NodeKey { get; private set; } = string.Empty;
    public string InstanceKey { get; private set; } = string.Empty;
    public string InstanceName { get; private set; } = string.Empty;
    public string ReportedStatus { get; private set; } = "unknown";
    public string HealthStatus { get; private set; } = "unknown";
    public Dictionary<string, string> Metadata { get; private set; } = [];
    public List<CapabilityDescriptor> Capabilities { get; private set; } = [];
    public InstanceHeartbeat? Heartbeat { get; private set; }
    public ICollection<InstanceStateHistory> StateHistory { get; private set; } = [];
    public ICollection<InstanceStatusChange> StatusChanges { get; private set; } = [];
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);

    public void UpdateRegistration(
        string organizationId,
        string environmentId,
        string applicationKey,
        string version,
        string nodeKey,
        string instanceName,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<CapabilityDescriptor> capabilities)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ApplicationKey = applicationKey;
        Version = version;
        NodeKey = nodeKey;
        InstanceName = instanceName;
        Metadata = new Dictionary<string, string>(metadata);
        Capabilities = capabilities.ToList();
    }

    public void RecordHeartbeat(DateTimeOffset heartbeatAtUtc, bool reachable, int latencyMs)
    {
        if (Heartbeat is null)
        {
            Heartbeat = new InstanceHeartbeat(Id, heartbeatAtUtc, reachable, latencyMs);
        }
        else
        {
            Heartbeat.Record(heartbeatAtUtc, reachable, latencyMs);
        }

        this.AddDomainEvent(new ApplicationHeartbeatRecordedDomainEvent(InstanceKey, heartbeatAtUtc, reachable));
    }

    public void RecordStateSnapshot(
        DateTimeOffset observedAtUtc,
        string reportedStatus,
        string healthStatus,
        string summary,
        IReadOnlyDictionary<string, string> metadata)
    {
        StateHistory.Add(new InstanceStateHistory(Id, observedAtUtc, reportedStatus, healthStatus, summary));

        if (!string.Equals(ReportedStatus, "unknown", StringComparison.Ordinal)
            && !string.Equals(ReportedStatus, reportedStatus, StringComparison.Ordinal))
        {
            StatusChanges.Add(new InstanceStatusChange(Id, ReportedStatus, reportedStatus, observedAtUtc));
            this.AddDomainEvent(new ApplicationInstanceStatusChangedDomainEvent(InstanceKey, ReportedStatus, reportedStatus, observedAtUtc));
        }

        ReportedStatus = reportedStatus;
        HealthStatus = healthStatus;
        Metadata = new Dictionary<string, string>(metadata);
        this.AddDomainEvent(new InstanceStateSnapshotRecordedDomainEvent(InstanceKey, observedAtUtc, reportedStatus, healthStatus));
    }
}

public class InstanceHeartbeat : Entity<InstanceHeartbeatId>
{
    protected InstanceHeartbeat()
    {
    }

    public InstanceHeartbeat(ApplicationInstanceId applicationInstanceId, DateTimeOffset lastHeartbeatAtUtc, bool reachable, int latencyMs)
    {
        Id = new InstanceHeartbeatId(Guid.CreateVersion7());
        ApplicationInstanceId = applicationInstanceId;
        Record(lastHeartbeatAtUtc, reachable, latencyMs);
    }

    public ApplicationInstanceId ApplicationInstanceId { get; private set; } = null!;
    public DateTimeOffset LastHeartbeatAtUtc { get; private set; }
    public bool Reachable { get; private set; }
    public int LatencyMs { get; private set; }

    public void Record(DateTimeOffset lastHeartbeatAtUtc, bool reachable, int latencyMs)
    {
        LastHeartbeatAtUtc = lastHeartbeatAtUtc;
        Reachable = reachable;
        LatencyMs = latencyMs;
    }
}

public class InstanceStateHistory : Entity<InstanceStateHistoryId>
{
    protected InstanceStateHistory()
    {
    }

    public InstanceStateHistory(ApplicationInstanceId applicationInstanceId, DateTimeOffset observedAtUtc, string reportedStatus, string healthStatus, string summary)
    {
        Id = new InstanceStateHistoryId(Guid.CreateVersion7());
        ApplicationInstanceId = applicationInstanceId;
        ObservedAtUtc = observedAtUtc;
        ReportedStatus = reportedStatus;
        HealthStatus = healthStatus;
        Summary = summary;
    }

    public ApplicationInstanceId ApplicationInstanceId { get; private set; } = null!;
    public DateTimeOffset ObservedAtUtc { get; private set; }
    public string ReportedStatus { get; private set; } = string.Empty;
    public string HealthStatus { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
}

public class InstanceStatusChange : Entity<InstanceStatusChangeId>
{
    protected InstanceStatusChange()
    {
    }

    public InstanceStatusChange(ApplicationInstanceId applicationInstanceId, string previousStatus, string currentStatus, DateTimeOffset changedAtUtc)
    {
        Id = new InstanceStatusChangeId(Guid.CreateVersion7());
        ApplicationInstanceId = applicationInstanceId;
        PreviousStatus = previousStatus;
        CurrentStatus = currentStatus;
        ChangedAtUtc = changedAtUtc;
    }

    public ApplicationInstanceId ApplicationInstanceId { get; private set; } = null!;
    public string PreviousStatus { get; private set; } = string.Empty;
    public string CurrentStatus { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; private set; }
}

public class RegistrationIdempotency : Entity<RegistrationIdempotencyId>, IAggregateRoot
{
    protected RegistrationIdempotency()
    {
    }

    public RegistrationIdempotency(string idempotencyKey, string registrationId, string instanceKey)
    {
        Id = new RegistrationIdempotencyId(Guid.CreateVersion7());
        IdempotencyKey = idempotencyKey;
        RegistrationId = registrationId;
        InstanceKey = instanceKey;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RegistrationId { get; private set; } = string.Empty;
    public string InstanceKey { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);
}
