namespace Nerv.IIP.Contracts.ConnectorProtocol;

public sealed record ConnectorRequestContext(
    string ProtocolVersion,
    string SdkVersion,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId);

public sealed record ApplicationRegistration(
    ConnectorRequestContext Context,
    string IdempotencyKey,
    string NodeKey,
    string NodeName,
    string DeploymentKind,
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string InstanceKey,
    string InstanceName,
    IReadOnlyList<CapabilityDescriptor> Capabilities,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ApplicationRegistrationResult(
    string RegistrationId,
    string InstanceKey,
    string IngestionToken);

public sealed record CapabilityDescriptor(
    string CapabilityCode,
    string CapabilityVersion,
    string Category,
    IReadOnlyList<string> SupportedOperations,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ApplicationHeartbeat(
    ConnectorRequestContext Context,
    string InstanceKey,
    DateTimeOffset HeartbeatAtUtc,
    bool Reachable,
    DateTimeOffset ConnectorHostStartedAtUtc,
    int LatencyMs,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record InstanceStateSnapshot(
    ConnectorRequestContext Context,
    string InstanceKey,
    DateTimeOffset ObservedAtUtc,
    string ReportedStatus,
    string HealthStatus,
    string Summary,
    IReadOnlyDictionary<string, string> Detail,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, string> Metadata,
    ConnectorCollectionHealth? CollectionHealth = null);

/// <summary>
/// Monotonic counters scoped to organization, environment, connector identity and counter epoch.
/// Received counts each raw source message/sample attempt exactly once. Dropped is the subset of received
/// input not accepted for processing. Error counts collection or processing exceptions and never reconnects.
/// Null counters mean no collection fact has yet been observed; zero is reported only after the first fact.
/// </summary>
public sealed record ConnectorCollectionHealth(
    string ConnectorId,
    string SourceSystem,
    Guid CounterEpoch,
    DateTimeOffset ReportedAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    DateTimeOffset? LastSampleAtUtc,
    ConnectorConnectionState? Connection = null);

public sealed record ConnectorConnectionState(
    string Status,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ConnectedSinceUtc = null,
    DateTimeOffset? DisconnectedSinceUtc = null,
    string? ReasonCategory = null,
    string? DiagnosticCode = null);

public sealed record ConnectorCollectionHealthResponse(
    string ConnectorId,
    string Status,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? MetricsReportedAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    string? SourceSystem,
    ConnectorConnectionState? Connection = null,
    string? StaleReason = null,
    string? OfflineReason = null,
    DateTimeOffset? HostLivenessDeadlineUtc = null);

/// <summary>
/// One connector-host instance's collection health, self-sufficient for a status wall card:
/// identity (<see cref="ConnectorId"/>), display name, protocol (<see cref="SourceSystem"/>: opcua/modbus/mqtt),
/// derived <see cref="Status"/> and the same heartbeat/throughput/drop facts as
/// <see cref="ConnectorCollectionHealthResponse"/>. Only unambiguous facts drive the status; nothing else is
/// asserted (a collection stall is NOT derived — see the note on the evaluator).
/// <para>
/// <see cref="Status"/> is one of: <c>current</c> (heartbeating, running, has produced a collection fact);
/// <c>stale</c> (see <see cref="StaleReason"/>); or <c>unknown</c> (heartbeating/running but no sampling
/// evidence yet — no received counter and no sample: an unconfigured/never-collected connector, "待采集";
/// not counted as online).
/// </para>
/// <para>
/// <see cref="StaleReason"/> disambiguates a <c>stale</c> status: <c>offline</c> means either an explicit field
/// connection loss or a Connector Host heartbeat timeout; <see cref="OfflineReason"/> identifies which one.
/// <c>fault</c> means the Host is fresh and the connector self-reported a terminal collector state. Null when
/// not stale. An explicit field loss takes precedence over a simultaneous Host timeout.
/// <see cref="CounterEpoch"/> scopes the monotonic counters; a consumer computing a sampling rate from
/// consecutive polls must reset its baseline when the epoch changes (a counter reset).
/// </para>
/// </summary>
public sealed record ConnectorCollectionHealthListItem(
    string ConnectorId,
    string ConnectorName,
    string Status,
    string? StaleReason,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? MetricsReportedAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    long? ReceivedCount,
    long? DroppedCount,
    long? ErrorCount,
    Guid? CounterEpoch,
    string? SourceSystem,
    ConnectorConnectionState? Connection = null,
    string? OfflineReason = null,
    DateTimeOffset? HostLivenessDeadlineUtc = null);

public sealed record ConnectorCollectionHealthListResponse(
    IReadOnlyList<ConnectorCollectionHealthListItem> Items,
    int Total);

public sealed record OperationResult(
    ConnectorRequestContext Context,
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string ExecutionStatus,
    FailureReason? Failure,
    IReadOnlyDictionary<string, string> Output);

public sealed record FailureReason(
    string Code,
    string Message,
    string Category,
    bool Retryable,
    IReadOnlyDictionary<string, string> Detail);
