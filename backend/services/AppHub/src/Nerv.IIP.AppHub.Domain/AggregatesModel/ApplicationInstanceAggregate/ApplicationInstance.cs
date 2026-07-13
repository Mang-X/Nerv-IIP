using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;

public partial record ApplicationInstanceId : IGuidStronglyTypedId;
public partial record InstanceHeartbeatId : IGuidStronglyTypedId;
public partial record InstanceStateHistoryId : IGuidStronglyTypedId;
public partial record InstanceStatusChangeId : IGuidStronglyTypedId;
public partial record RegistrationIdempotencyId : IGuidStronglyTypedId;
public partial record ConnectorCollectionHealthProjectionId : IGuidStronglyTypedId;

public class ApplicationInstance : Entity<ApplicationInstanceId>, IAggregateRoot
{
    protected ApplicationInstance()
    {
    }

    public ApplicationInstance(
        string organizationId,
        string environmentId,
        string connectorHostId,
        string applicationKey,
        string version,
        string nodeKey,
        string instanceKey,
        string instanceName,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<CapabilityDescriptor> capabilities)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ConnectorHostId = connectorHostId;
        ApplicationKey = applicationKey;
        Version = version;
        NodeKey = nodeKey;
        InstanceKey = instanceKey;
        InstanceName = instanceName;
        Metadata = new Dictionary<string, string>(metadata);
        Capabilities = capabilities.ToList();
        this.AddDomainEvent(new ApplicationInstanceRegisteredDomainEvent(organizationId, environmentId, instanceKey));
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
        : this(
            organizationId,
            environmentId,
            string.Empty,
            applicationKey,
            version,
            nodeKey,
            instanceKey,
            instanceName,
            metadata,
            capabilities)
    {
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ConnectorHostId { get; private set; } = string.Empty;
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
    public ConnectorCollectionHealthProjection? CollectionHealth { get; private set; }
    public ICollection<InstanceStateHistory> StateHistory { get; private set; } = [];
    public ICollection<InstanceStatusChange> StatusChanges { get; private set; } = [];
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);

    public void UpdateRegistration(
        string organizationId,
        string environmentId,
        string connectorHostId,
        string applicationKey,
        string version,
        string nodeKey,
        string instanceName,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<CapabilityDescriptor> capabilities)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ConnectorHostId = connectorHostId;
        ApplicationKey = applicationKey;
        Version = version;
        NodeKey = nodeKey;
        InstanceName = instanceName;
        Metadata = new Dictionary<string, string>(metadata);
        Capabilities = capabilities.ToList();
    }

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
        UpdateRegistration(
            organizationId,
            environmentId,
            ConnectorHostId,
            applicationKey,
            version,
            nodeKey,
            instanceName,
            metadata,
            capabilities);
    }

    public void RecordHeartbeat(DateTimeOffset heartbeatAtUtc, bool reachable, int latencyMs)
    {
        var wasUnreachable = Heartbeat is not null && !Heartbeat.Reachable;
        if (Heartbeat is null)
        {
            Heartbeat = new InstanceHeartbeat(Id, heartbeatAtUtc, reachable, latencyMs);
        }
        else
        {
            Heartbeat.Record(heartbeatAtUtc, reachable, latencyMs);
        }

        this.AddDomainEvent(new ApplicationHeartbeatRecordedDomainEvent(InstanceKey, heartbeatAtUtc, reachable));
        if (wasUnreachable && reachable && !string.IsNullOrWhiteSpace(ConnectorHostId))
        {
            this.AddDomainEvent(new ConnectorHostRestoredDomainEvent(
                OrganizationId,
                EnvironmentId,
                ConnectorHostId,
                InstanceKey,
                heartbeatAtUtc));
        }
    }

    public bool MarkHeartbeatUnreachable(DateTimeOffset detectedAtUtc, TimeSpan heartbeatTimeout)
    {
        if (Heartbeat is null || !Heartbeat.Reachable || string.IsNullOrWhiteSpace(ConnectorHostId))
        {
            return false;
        }

        if (Heartbeat.LastHeartbeatAtUtc.Add(heartbeatTimeout) > detectedAtUtc)
        {
            return false;
        }

        Heartbeat.MarkUnreachable();
        this.AddDomainEvent(new ConnectorHostUnreachableDomainEvent(
            OrganizationId,
            EnvironmentId,
            ConnectorHostId,
            InstanceKey,
            Heartbeat.LastHeartbeatAtUtc,
            detectedAtUtc,
            heartbeatTimeout));
        return true;
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

    public void RecordCollectionHealth(ConnectorCollectionHealth report)
    {
        if (!string.Equals(report.ConnectorId, InstanceKey, StringComparison.Ordinal))
        {
            throw new ArgumentException("Collection health connector identity must match the registered instance.", nameof(report));
        }

        if (CollectionHealth is null)
        {
            CollectionHealth = new ConnectorCollectionHealthProjection(Id, OrganizationId, EnvironmentId, report);
            return;
        }

        CollectionHealth.Record(report);
    }

    public bool RecordOperationTaskCompletedRefresh(
        string idempotencyKey,
        string operationTaskId,
        string operationCode,
        DateTimeOffset finishedAtUtc,
        string correlationId)
    {
        var processedKey = GetCompletedOperationMetadataKey(idempotencyKey);
        if (Metadata.ContainsKey(processedKey))
        {
            return false;
        }

        StateHistory.Add(new InstanceStateHistory(
            Id,
            finishedAtUtc,
            ReportedStatus,
            HealthStatus,
            $"Operation task {operationTaskId} completed: {operationCode}"));
        Metadata = new Dictionary<string, string>(Metadata)
        {
            [processedKey] = finishedAtUtc.ToString("O"),
            ["ops.lastCompletedOperationIdempotencyKey"] = idempotencyKey,
            ["ops.lastCompletedOperationTaskId"] = operationTaskId,
            ["ops.lastCompletedOperationCode"] = operationCode,
            ["ops.lastCompletedOperationCorrelationId"] = correlationId
        };
        this.AddDomainEvent(new InstanceStateSnapshotRecordedDomainEvent(InstanceKey, finishedAtUtc, ReportedStatus, HealthStatus));

        return true;
    }

    public bool RecordOperationTaskFailedRefresh(
        string idempotencyKey,
        string operationTaskId,
        string operationCode,
        DateTimeOffset finishedAtUtc,
        string correlationId,
        string? failureCode)
    {
        var processedKey = GetFailedOperationMetadataKey(idempotencyKey);
        if (Metadata.ContainsKey(processedKey))
        {
            return false;
        }

        var failureSummary = string.IsNullOrWhiteSpace(failureCode)
            ? $"Operation task {operationTaskId} failed: {operationCode}"
            : $"Operation task {operationTaskId} failed: {operationCode} ({failureCode})";
        StateHistory.Add(new InstanceStateHistory(
            Id,
            finishedAtUtc,
            ReportedStatus,
            HealthStatus,
            failureSummary));
        Metadata = new Dictionary<string, string>(Metadata)
        {
            [processedKey] = finishedAtUtc.ToString("O"),
            ["ops.lastFailedOperationIdempotencyKey"] = idempotencyKey,
            ["ops.lastFailedOperationTaskId"] = operationTaskId,
            ["ops.lastFailedOperationCode"] = operationCode,
            ["ops.lastFailedOperationCorrelationId"] = correlationId,
            ["ops.lastFailedOperationFailureCode"] = failureCode ?? string.Empty
        };
        this.AddDomainEvent(new InstanceStateSnapshotRecordedDomainEvent(InstanceKey, finishedAtUtc, ReportedStatus, HealthStatus));

        return true;
    }

    private static string GetCompletedOperationMetadataKey(string idempotencyKey)
    {
        return $"ops.completed.{idempotencyKey}";
    }

    private static string GetFailedOperationMetadataKey(string idempotencyKey)
    {
        return $"ops.failed.{idempotencyKey}";
    }
}

public class ConnectorCollectionHealthProjection : Entity<ConnectorCollectionHealthProjectionId>
{
    protected ConnectorCollectionHealthProjection() { }

    public ConnectorCollectionHealthProjection(ApplicationInstanceId applicationInstanceId, string organizationId, string environmentId, ConnectorCollectionHealth report)
    {
        ApplicationInstanceId = applicationInstanceId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ConnectorId = report.ConnectorId;
        Record(report);
    }

    public ApplicationInstanceId ApplicationInstanceId { get; private set; } = null!;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ConnectorId { get; private set; } = string.Empty;
    public string SourceSystem { get; private set; } = string.Empty;
    public Guid CounterEpoch { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }
    public long? ReceivedCount { get; private set; }
    public long? DroppedCount { get; private set; }
    public long? ErrorCount { get; private set; }
    public DateTimeOffset? LastSampleAtUtc { get; private set; }
    public string RetiredCounterEpochs { get; private set; } = string.Empty;

    public void Record(ConnectorCollectionHealth report)
    {
        var isNewEpoch = CounterEpoch != Guid.Empty && report.CounterEpoch != CounterEpoch;
        if (isNewEpoch)
        {
            var retired = RetiredCounterEpochs.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (retired.Contains(report.CounterEpoch.ToString("N"))) return;
            retired.Add(CounterEpoch.ToString("N"));
            RetiredCounterEpochs = string.Join(',', retired.TakeLast(16));
        }
        else if (ReportedAtUtc >= report.ReportedAtUtc) return;
        if (CounterEpoch == report.CounterEpoch &&
            (IsDecrease(ReceivedCount, report.ReceivedCount) || IsDecrease(DroppedCount, report.DroppedCount) || IsDecrease(ErrorCount, report.ErrorCount))) return;
        ConnectorId = report.ConnectorId;
        SourceSystem = report.SourceSystem;
        CounterEpoch = report.CounterEpoch;
        ReportedAtUtc = report.ReportedAtUtc;
        ReceivedCount = isNewEpoch ? report.ReceivedCount : report.ReceivedCount ?? ReceivedCount;
        DroppedCount = isNewEpoch ? report.DroppedCount : report.DroppedCount ?? DroppedCount;
        ErrorCount = isNewEpoch ? report.ErrorCount : report.ErrorCount ?? ErrorCount;
        LastSampleAtUtc = isNewEpoch ? report.LastSampleAtUtc : report.LastSampleAtUtc ?? LastSampleAtUtc;
    }

    private static bool IsDecrease(long? previous, long? current) => previous.HasValue && current.HasValue && current < previous;
}

public class InstanceHeartbeat : Entity<InstanceHeartbeatId>
{
    protected InstanceHeartbeat()
    {
    }

    public InstanceHeartbeat(ApplicationInstanceId applicationInstanceId, DateTimeOffset lastHeartbeatAtUtc, bool reachable, int latencyMs)
    {
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

    public void MarkUnreachable()
    {
        Reachable = false;
    }
}

public class InstanceStateHistory : Entity<InstanceStateHistoryId>
{
    protected InstanceStateHistory()
    {
    }

    public InstanceStateHistory(ApplicationInstanceId applicationInstanceId, DateTimeOffset observedAtUtc, string reportedStatus, string healthStatus, string summary)
    {
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

    public RegistrationIdempotency(string organizationId, string environmentId, string idempotencyKey, string registrationId, string instanceKey)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        IdempotencyKey = idempotencyKey;
        RegistrationId = registrationId;
        InstanceKey = instanceKey;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RegistrationId { get; private set; } = string.Empty;
    public string InstanceKey { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);
}
